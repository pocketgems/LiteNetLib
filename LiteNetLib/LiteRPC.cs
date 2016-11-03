using System;
using System.Collections.Generic;
using System.Reflection;

namespace LiteNetLib.Utils
{
    public class RPCMethod : Attribute
    {
        
    }

    public sealed class LiteRPC
    {
        private enum RPCType
        {
            Method,
            Func
        }

        private struct RegisteredObjectInfo
        {
            public object Object;
            public Type Type;
        }

        private struct ReadWriteDelegates
        {
            public WriteDelegate WriteDelegate;
            public ReadDelegate ReadDelegate;
        }

        private class MethodData
        {
            private readonly MethodInfo _methodInfo;
            private readonly WriteDelegate[] _writeDelegates;
            private readonly ReadDelegate[] _readDelegates;
            private readonly object[] _invokeParams;

            public MethodData(MethodInfo methodInfo, WriteDelegate[] writeDelegates, ReadDelegate[] readDelegates)
            {
                _writeDelegates = writeDelegates;
                _readDelegates = readDelegates;
                _methodInfo = methodInfo;
                _invokeParams = new object[readDelegates.Length];
            }

            public object Invoke(NetDataReader reader, object targetObject)
            {
                for (int i = 0; i < _readDelegates.Length; i++)
                {
                    _invokeParams[i] = _readDelegates[i](reader);
                }
                return _methodInfo.Invoke(targetObject, _invokeParams);
            }

            public void WriteMethodCall(NetDataWriter writer, object[] args)
            {
                if (args.Length != _invokeParams.Length)
                {
                    throw new ArgumentException("Invalid argument count: " + args.Length + ", expected: " + _invokeParams.Length);
                }

                for (int i = 0; i < _writeDelegates.Length; i++)
                {
                    _writeDelegates[i](writer, args[i]);
                }
            }
        }

        private class ObjectMethods
        {
            public readonly Dictionary<string, MethodData> Delegates = new Dictionary<string, MethodData>();
        }

        private struct PendingResponse
        {
            public RPCCallbackDelegate Callback;
            public Type Type;
        }

        public delegate void WriteDelegate(NetDataWriter writer, object o);
        public delegate object ReadDelegate(NetDataReader reader);
        private delegate void RPCCallbackDelegate(object o);

        private readonly Dictionary<Type, ObjectMethods> _objectMethodInfos = new Dictionary<Type, ObjectMethods>();
        private readonly Dictionary<string, RegisteredObjectInfo> _registeredObjects = new Dictionary<string, RegisteredObjectInfo>();
        private readonly Dictionary<Type, ReadWriteDelegates> _registeredCustomTypes = new Dictionary<Type, ReadWriteDelegates>();
        private readonly Dictionary<int, PendingResponse> _pendingCallbacks = new Dictionary<int, PendingResponse>();
        private readonly Dictionary<Type, ReadWriteDelegates> _resultTypes = new Dictionary<Type, ReadWriteDelegates>();

        public const int MaxMethodNameLength = 256;
        public const int MaxStringLenght = 512;
        private ushort _lastCallId;

        public void RegisterObject<T>(T obj)
        {
            Type t = typeof(T);
            if (_registeredObjects.ContainsKey(t.Name))
            {
                throw new Exception("You already registered: " + t);
            }
            _registeredObjects.Add(t.Name, new RegisteredObjectInfo { Object = obj, Type = t });
            if (!_objectMethodInfos.ContainsKey(t))
            {
                RegisterType(t);
            }
        }

        public void RemoveObject<T>(T obj)
        {
            Type t = typeof(T);
            if (!_registeredObjects.Remove(t.Name))
            {
                throw new Exception("Object:" + t.Name + " is not registered");
            }
        }

        public void RegisterCustomType<T>(WriteDelegate writeMethod, ReadDelegate readMethod)
        {
            _registeredCustomTypes.Add(typeof(T), new ReadWriteDelegates { ReadDelegate = readMethod, WriteDelegate = writeMethod });
        }

        public void ProcessResult(NetDataReader data)
        {
            ushort callId = data.GetUShort();
            PendingResponse pr;
            ReadWriteDelegates rwd;
            if (_pendingCallbacks.TryGetValue(callId, out pr) && _resultTypes.TryGetValue(pr.Type, out rwd))
            {
                var result = rwd.ReadDelegate(data);
                pr.Callback(result);
            }
        }

        public void ExecuteData(NetPeer from, NetDataReader data)
        {
            var rpcType = (RPCType) data.GetByte();
            ushort callId = 0;
            if (rpcType == RPCType.Func)
            {
                callId = data.GetUShort();
            }

            var objectId = data.GetString(MaxMethodNameLength);
            RegisteredObjectInfo objectInfo;
            if (!_registeredObjects.TryGetValue(objectId, out objectInfo))
            {
                return;
            }

            ObjectMethods om;
            if (!_objectMethodInfos.TryGetValue(objectInfo.Type, out om))
            {
                return;
            }

            string methodName = data.GetString(MaxMethodNameLength);
            MethodData methodData;
            if (!om.Delegates.TryGetValue(methodName, out methodData))
            {
                return;
            }
            
            object result = methodData.Invoke(data, objectInfo.Object);
            if (rpcType == RPCType.Func)
            {
                
            }
        }

        private void RegisterType(Type t)
        {
            var methodInfo = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var attrType = typeof(RPCMethod);
            var rpcMethods = new List<MethodInfo>(methodInfo.Length);

            for (int i = 0; i < methodInfo.Length; i++)
            {
                if(methodInfo[i].GetCustomAttributes(attrType, false).Length == 0)
                    continue;
                rpcMethods.Add(methodInfo[i]);
            }

            if (rpcMethods.Count == 0)
            {
                throw new ArgumentException("No RPCMethods found");
            }

            ObjectMethods om = new ObjectMethods();
            for (int i = 0; i < rpcMethods.Count; i++)
            {
                var method = rpcMethods[i];
                var param = method.GetParameters();
                var writeDelegates = new WriteDelegate[param.Length];
                var readDelegates = new ReadDelegate[param.Length];

                for (int j = 0; j < param.Length; j++)
                {
                    Type paramType = param[j].ParameterType;
                    WriteDelegate objWrite;
                    ReadDelegate objRead;
                    if (paramType.IsArray)
                    {
                        Type elementType = paramType.GetElementType();
                        AssignDelegates(elementType, out objWrite, out objRead);
                        writeDelegates[j] = (writer, o) =>
                        {
                            Array arr = (Array) o;
                            writer.Put(arr.Length);
                            for (int idx = 0; idx < arr.Length; idx++)
                            {
                                objWrite(writer, arr.GetValue(idx));
                            }
                        };
                        readDelegates[j] = reader =>
                        {
                            int elemCount = reader.GetInt();
                            Array arr = Array.CreateInstance(elementType, elemCount);
                            for (int idx = 0; idx < elemCount; idx++)
                            {
                                object value = objRead(reader);
                                arr.SetValue(value, idx);
                            }
                            return arr;
                        };
                    }
                    else
                    {
                        AssignDelegates(param[j].ParameterType, out objWrite, out objRead);
                        writeDelegates[j] = objWrite;
                        readDelegates[j] = objRead;
                    }
                }
                om.Delegates.Add(method.Name, new MethodData(method, writeDelegates, readDelegates));
            }
            _objectMethodInfos.Add(t, om);
        }

        private void AssignDelegates(Type t, out WriteDelegate writeDelegate, out ReadDelegate readDelegate)
        {
            ReadWriteDelegates registeredCustomType;

            if (t == typeof(string))
            {
                writeDelegate = (writer, o) => { writer.Put((string)o, MaxStringLenght); };
                readDelegate = reader => reader.GetString(MaxStringLenght);
            }
            else if (t == typeof(byte))
            {
                writeDelegate = (writer, o) => { writer.Put((byte)o); };
                readDelegate = reader => reader.GetByte();
            }
            else if (t == typeof(sbyte))
            {
                writeDelegate = (writer, o) => { writer.Put((sbyte)o); };
                readDelegate = reader => reader.GetSByte();
            }
            else if (t == typeof(short))
            {
                writeDelegate = (writer, o) => { writer.Put((short)o); };
                readDelegate = reader => reader.GetShort();
            }
            else if (t == typeof(ushort))
            {
                writeDelegate = (writer, o) => { writer.Put((ushort)o); };
                readDelegate = reader => reader.GetUShort();
            }
            else if (t == typeof(int))
            {
                writeDelegate = (writer, o) => { writer.Put((int)o); };
                readDelegate = reader => reader.GetInt();
            }
            else if (t == typeof(uint))
            {
                writeDelegate = (writer, o) => { writer.Put((uint)o); };
                readDelegate = reader => reader.GetUInt();
            }
            else if (t == typeof(long))
            {
                writeDelegate = (writer, o) => { writer.Put((long)o); };
                readDelegate = reader => reader.GetLong();
            }
            else if (t == typeof(ulong))
            {
                writeDelegate = (writer, o) => { writer.Put((ulong)o); };
                readDelegate = reader => reader.GetULong();
            }
            else if (t == typeof(float))
            {
                writeDelegate = (writer, o) => { writer.Put((float)o); };
                readDelegate = reader => reader.GetFloat();
            }
            else if (t == typeof(double))
            {
                writeDelegate = (writer, o) => { writer.Put((double)o); };
                readDelegate = reader => reader.GetDouble();
            }
            else if (_registeredCustomTypes.TryGetValue(t, out registeredCustomType))
            {
                writeDelegate = registeredCustomType.WriteDelegate;
                readDelegate = registeredCustomType.ReadDelegate;
            }
            else
            {
                throw new ArgumentException("Unregistered argument type: " + t);
            }
        }

        public void CallClassMethod<TClass>(NetDataWriter writer, string methodName, params object[] args)
        {
            Type classType = typeof(TClass);
            ObjectMethods om;
            if (!_objectMethodInfos.TryGetValue(classType, out om))
            {
                RegisterType(classType);
            }

            MethodData methodData;
            if (!om.Delegates.TryGetValue(methodName, out methodData))
            {
                throw new ArgumentException("Method: " + methodName + " does'nt exist in " + classType.Name + " class");
            }

            writer.Put((byte)RPCType.Method);
            writer.Put(classType.Name, MaxMethodNameLength);
            writer.Put(methodName, MaxMethodNameLength);
            methodData.WriteMethodCall(writer, args);
        }

        public void CallClassFunc<TRet, TClass>(NetDataWriter writer, Action<TRet> resultCallback, string methodName, params object[] args)
        {
            Type classType = typeof(TClass);
            ObjectMethods om;
            if (!_objectMethodInfos.TryGetValue(classType, out om))
            {
                RegisterType(classType);
            }

            MethodData methodData;
            if (!om.Delegates.TryGetValue(methodName, out methodData))
            {
                throw new ArgumentException("Method: " + methodName + " does'nt exist in " + classType.Name + " class");
            }

            writer.Put((byte)RPCType.Func);
            writer.Put(_lastCallId);
            writer.Put(classType.Name, MaxMethodNameLength);
            writer.Put(methodName, MaxMethodNameLength);
            methodData.WriteMethodCall(writer, args);

            ReadWriteDelegates rwd = new ReadWriteDelegates();
            Type resultType = typeof(TRet);
            AssignDelegates(resultType, out rwd.WriteDelegate, out rwd.ReadDelegate);
            _resultTypes.Add(resultType, rwd);
            _pendingCallbacks.Add(_lastCallId, new PendingResponse { Callback = obj => resultCallback((TRet)obj), Type = resultType });

            _lastCallId++;
        }
    }
}
