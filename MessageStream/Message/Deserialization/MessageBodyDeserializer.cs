using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MessageStream.Message
{

    public class MessageBodyDeserializer<TIdentifier, TMessage, TState>
    {

        internal delegate TMessage DeserializeMessageDelegate(in ReadOnlySpan<byte> buffer, TState state, TIdentifier identifier);

        private readonly Dictionary<TIdentifier, DeserializeMessageDelegate> deserializerFuncs = new Dictionary<TIdentifier, DeserializeMessageDelegate>();

        private readonly IMessageProvider<TIdentifier, TMessage> messageProvider;
        private readonly IMessageBodyDeserializer<TIdentifier>[] deserializers;

        public MessageBodyDeserializer(
            IMessageProvider<TIdentifier, TMessage> messageProvider,
            params IMessageBodyDeserializer<TIdentifier>[] deserializers
        )
        {
            this.messageProvider = messageProvider;
            this.deserializers = deserializers;
            Initialize();
        }

        private void Initialize()
        {
            foreach (var deserializer in deserializers)
            {
                DeserializeMessageDelegate deserializeDelegate = BuildDeserializeDelegate(deserializer);
                deserializerFuncs.Add(deserializer.Identifier, deserializeDelegate);
            }
        }

        public TMessage Deserialize(in ReadOnlySpan<byte> buffer, TState state, TIdentifier identifier)
        {
            DeserializeMessageDelegate deserializeFunc = null;
            if (!deserializerFuncs.TryGetValue(identifier, out deserializeFunc))
            {
                return default;
            }
            return deserializeFunc(in buffer, state, identifier);
        }

        private DeserializeMessageDelegate BuildDeserializeDelegate(IMessageBodyDeserializer<TIdentifier> deserializer)
        {
            const string deserializeMethodName = "Deserialize";
            const string deserializeBodyMethodName = nameof(IMessageBodyDeserializer<TIdentifier, TState, TMessage>.DeserializeOnto);

            var deserializerType = deserializer.GetType();
            var (realMessageType, interfaceType) = ResolveRealMessageType(deserializer.GetType());

            var deserializeMethod = deserializerType.GetMethod(deserializeBodyMethodName);

            // This doesn't work. I do not know why. ;(
            if (deserializeMethod == null)
            {
                //var map = deserializerType.GetInterfaceMap(interfaceType);
                //var interfaceMethod = interfaceType.GetMethod(deserializeBodyMethodName);

                deserializerType = typeof(IMessageBodyDeserializer<,,>).MakeGenericType(typeof(TIdentifier), typeof(TState), realMessageType);
                var genericInterfaceMethod = deserializerType.GetMethod(nameof(IMessageBodyDeserializer<TIdentifier, TState, TMessage>.DeserializeOnto));

                bool same = deserializerType == interfaceType;
                //deserializerType = map.InterfaceType;
                //deserializeMethod = map.TargetMethods[Array.IndexOf(map.InterfaceMethods, interfaceMethod)];
            }

            var typeBuilder = CreateTypeBuilderForDeserializer(realMessageType.Name + "Body");

            // Generate constructor
            var deserializerField = typeBuilder.DefineField("deserializer", deserializerType, FieldAttributes.Private);
            var messageProviderField = typeBuilder.DefineField("messageProvider", typeof(IMessageProvider<TIdentifier, TMessage>), FieldAttributes.Private);

            var constructor = typeBuilder.DefineConstructor(
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, CallingConventions.HasThis, new[] { deserializer.GetType(), messageProvider.GetType() });

            var constructorIlGenerator = constructor.GetILGenerator();

            constructorIlGenerator.Emit(OpCodes.Ldarg_0);
            constructorIlGenerator.Emit(OpCodes.Ldarg_1);
            constructorIlGenerator.Emit(OpCodes.Stfld, deserializerField);

            constructorIlGenerator.Emit(OpCodes.Ldarg_0);
            constructorIlGenerator.Emit(OpCodes.Ldarg_2);
            constructorIlGenerator.Emit(OpCodes.Stfld, messageProviderField);
            constructorIlGenerator.Emit(OpCodes.Ret);

            // Generate method
            var deserializeMethodBuilder = typeBuilder.DefineMethod(
                deserializeMethodName,
                MethodAttributes.Public | MethodAttributes.HideBySig |
                MethodAttributes.Virtual | MethodAttributes.Final,
                CallingConventions.HasThis,
                typeof(TMessage),
                new Type[0],
                new Type[0],
                new[]
                {
                    typeof(ReadOnlySpan<byte>).MakeByRefType(),
                    typeof(TState),
                    typeof(TIdentifier)
                },
                new[]
                {
                    new [] { typeof(InAttribute) },
                    new Type[0],
                    new Type[0]
                },
                new[]
                {
                    new Type[0],
                    new Type[0],
                    new Type[0]
                });

            deserializeMethodBuilder
                .DefineParameter(1, ParameterAttributes.In, "buffer")
                .SetCustomAttribute(typeof(IsReadOnlyAttribute).GetConstructors()[0], new byte[] { 01, 00, 00, 00 });

            // Build method body
            var methodIlGenerator = deserializeMethodBuilder.GetILGenerator();
            
            var messageLocal = methodIlGenerator.DeclareLocal(realMessageType);

            methodIlGenerator.Emit(OpCodes.Ldarg_0);
            methodIlGenerator.Emit(OpCodes.Ldfld, messageProviderField);

            methodIlGenerator.Emit(OpCodes.Ldarg_3);

            var getMessageMethod = typeof(IMessageProvider<TIdentifier, TMessage>).GetMethod(nameof(IMessageProvider<TIdentifier, TMessage>.GetMessage)).MakeGenericMethod(realMessageType);
            methodIlGenerator.Emit(OpCodes.Callvirt, getMessageMethod);
            methodIlGenerator.Emit(OpCodes.Stloc, messageLocal);

            methodIlGenerator.Emit(OpCodes.Ldarg_0);
            methodIlGenerator.Emit(OpCodes.Ldfld, deserializerField);
            methodIlGenerator.Emit(OpCodes.Ldarg_1);
            methodIlGenerator.Emit(OpCodes.Ldarg_2);
            methodIlGenerator.Emit(OpCodes.Ldloca, messageLocal);

            if (deserializer.GetType() == deserializerType)
            {
                methodIlGenerator.Emit(OpCodes.Call, deserializeMethod);
            }
            else
            {
                methodIlGenerator.Emit(OpCodes.Callvirt, deserializeMethod);
            }

            methodIlGenerator.Emit(OpCodes.Ldloc, messageLocal);
            methodIlGenerator.Emit(OpCodes.Ret);

            var type = typeBuilder.CreateType();
            var obj = Activator.CreateInstance(type, new object[] { deserializer, messageProvider });
            
            return (DeserializeMessageDelegate) type.GetMethod(deserializeMethodName).CreateDelegate(typeof(DeserializeMessageDelegate), obj);
        }

        private (Type messageType, Type interfaceType) ResolveRealMessageType(Type type)
        {
            Type baseType = type;

            while (baseType != typeof(object) && baseType != null && baseType != typeof(IMessageBodyDeserializer<,,>))
            {
                var iface = baseType.GetInterfaces()
                    .FirstOrDefault(t => t.GetGenericTypeDefinition() == typeof(IMessageBodyDeserializer<,,>));

                if (iface != null)
                {
                    return (iface.GenericTypeArguments[2], iface);
                }

                baseType = baseType.BaseType;
            }

            throw new Exception($"Cannot find real message type for type {type}");
        }

        private static TypeBuilder CreateTypeBuilderForDeserializer(string name)
        {
            var typeSignature = $"{name}{Guid.NewGuid().ToString().Replace("-", "")}";
            var an = new AssemblyName(typeSignature);
            AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
            ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule($"{name}{Guid.NewGuid().ToString()}Module");
            TypeBuilder tb = moduleBuilder.DefineType(typeSignature,
                    TypeAttributes.Public |
                    TypeAttributes.Class |
                    TypeAttributes.AutoClass |
                    TypeAttributes.AnsiClass |
                    TypeAttributes.BeforeFieldInit |
                    TypeAttributes.AutoLayout,
                    null);
            return tb;
        }

    }

}
