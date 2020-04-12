using System;
using System.Collections.Generic;

namespace MessageStream
{
    public class RequestResponseKeyResolver<T>
    {

#nullable disable

        private Dictionary<Type, Func<T, string>> keyFactories = new Dictionary<Type, Func<T, string>>();
        private Func<T, string> globalKeyFactory;

        public RequestResponseKeyResolver(Action<RequestResponseKeyResolver<T>> configure = null)
        {
            configure?.Invoke(this);
        }

        public RequestResponseKeyResolver<T> AddByTypeName(params Type[] types)
        {
            foreach(var type in types)
            {
                keyFactories.Add(type, (msg) => type.AssemblyQualifiedName);
            }
            return this;
        }

        public RequestResponseKeyResolver<T> AddStaticKeyForTypes(string key, params Type[] types)
        {
            foreach (var type in types)
            {
                keyFactories.Add(type, (msg) => key);
            }
            return this;
        }

        public void AddGlobalResolver(Func<T, string> resolver)
        {
            this.globalKeyFactory = resolver;
        }

        public RequestResponseKeyResolver<T> AddResolver<TMessage>(Func<TMessage, string> resolver)
        {
            keyFactories.Add(typeof(TMessage), (msg) => msg is TMessage casted ? resolver(casted) : null);
            return this;
        }

#nullable enable
        public string? GetKey(T message)
        {
            // Check the global key factory first
            if (globalKeyFactory != null)
            {
                var key = globalKeyFactory(message);
                if (key != null)
                {
                    return key;
                }
            }

            // Try to find one for this specific type
            if (keyFactories.TryGetValue(message.GetType(), out var keyFactory))
            {
                return keyFactory(message);
            }

            // None found
            return null;
        }
#nullable restore

    }
}