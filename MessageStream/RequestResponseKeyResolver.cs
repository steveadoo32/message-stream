using System;
using System.Collections.Generic;

namespace MessageStream
{
    public class RequestResponseKeyResolver<T>
    {

        private Dictionary<Type, Func<T, string>> keyFactories = new Dictionary<Type, Func<T, string>>();

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

        public RequestResponseKeyResolver<T> AddStaticForTypes(string key, params Type[] types)
        {
            foreach (var type in types)
            {
                keyFactories.Add(type, (msg) => key);
            }
            return this;
        }

        public RequestResponseKeyResolver<T> AddResolver<TMessage>(Func<TMessage, string> resolver)
        {
            keyFactories.Add(typeof(TMessage), (msg) => msg is TMessage casted ? resolver(casted) : null);
            return this;
        }

        public string GetKey(T message)
        {
            if (keyFactories.TryGetValue(message.GetType(), out var keyFactory))
            {
                return keyFactory(message);
            }
            return null;
        }

    }
}