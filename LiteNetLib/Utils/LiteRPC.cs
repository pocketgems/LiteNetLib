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
        private delegate void WriteDelegate(NetDataWriter writer, object o);
        private delegate void ReadDelegate(NetDataReader reader, object o);

        private readonly Dictionary<Type, ReadDelegate[]> _objectReaders = new Dictionary<Type, ReadDelegate[]>();
        private readonly Dictionary<Type, WriteDelegate[]> _objectWriters = new Dictionary<Type, WriteDelegate[]>();
        private readonly Dictionary<ushort, object> _registeredObjects = new Dictionary<ushort, object>();

        private NetDataWriter _netDataWriter = new NetDataWriter();

        public void RegisterObject<T>(ushort id, T obj)
        {
            Type t = typeof(T);
            
        }

        public void ParseData()
        {
            ReadDelegate[] readDelegates;
            if (!_objectReaders.TryGetValue(t, out readDelegates))
            {
                RegisterType(t);
                readDelegates = _objectReaders[t];
            }
        }

        private void RegisterType(Type t)
        {
            var methodInfo = t.GetMethods(BindingFlags.Public);
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

            for (int i = 0; i < rpcMethods.Count; i++)
            {
                var method = rpcMethods[i];
                var param = method.GetParameters();
                
            }
        }

        public void CallClassMethod<T>(ushort id, string methodName, params object[] args)
        {
            Type t = typeof(T);
            WriteDelegate[] writeDelegates;
            if (!_objectWriters.TryGetValue(t, out writeDelegates))
            {
                RegisterType(t);
                writeDelegates = _objectWriters[t];
            }

            if (args.Length != writeDelegates.Length)
            {
                throw new ArgumentException("Invalid argument count: " + args.Length + ", expected: " + writeDelegates.Length);
            }

            for (int i = 0; i < writeDelegates.Length; i++)
            {
                writeDelegates[i](_netDataWriter, args[i]);
            }
        }
    }
}
