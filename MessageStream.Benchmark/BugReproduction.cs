using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace MessageStream.Benchmark
{

    /// <summary>
    /// Bug in .net core 2.0 :(. Need to wait for 3.
    /// </summary>
    public class BugReproduction
    {

        public static void Main(string[] args)
        {
            var test = new TestClass<int>();
            var span = new ReadOnlySpan<byte>(new byte[] { 1 });
            test.OuterDoSomething(in span, 10);
        }

    }

    public class TestClass<T> where T : new()
    {

        private ITestInterface<T> testInterfaceImpl;

        public TestClass()
        {
            Initialize();
        }

        public T OuterDoSomething(in ReadOnlySpan<byte> memory, T o)
        {
            return testInterfaceImpl.DoSomething(in memory, o);
        }

        private void Initialize()
        {
            Type concreteType = GetType();
            Type interfaceType = typeof(ITestInterface<T>);

            var methodToOverride = interfaceType.GetMethod(nameof(ITestInterface<T>.DoSomething));
            string overrideMethodName = string.Format("{0}.{1}", interfaceType.FullName, methodToOverride.Name);

            var typeBuilder = CreateTypeBuilderForDeserializer(GetType().Name);

            var thisField = typeBuilder.DefineField("testClass", concreteType, FieldAttributes.Private);

            var constructor = typeBuilder.DefineConstructor(
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, CallingConventions.HasThis, new[] { concreteType });

            var constructorIlGenerator = constructor.GetILGenerator();

            constructorIlGenerator.Emit(OpCodes.Ldarg_0);
            constructorIlGenerator.Emit(OpCodes.Ldarg_1);
            constructorIlGenerator.Emit(OpCodes.Stfld, thisField);
            constructorIlGenerator.Emit(OpCodes.Ret);

            var doSomethingMethodBuilder = typeBuilder.DefineMethod(
                overrideMethodName,
                MethodAttributes.Public | MethodAttributes.HideBySig |
                MethodAttributes.Virtual | MethodAttributes.Final,
                CallingConventions.HasThis,
                typeof(T),
                new Type[0],
                new Type[0],
                new[] 
                {
                    typeof(ReadOnlySpan<byte>).MakeByRefType(),
                    typeof(T)
                },
                new[]
                {
                    new [] { typeof(InAttribute) },
                    new Type[0]
                },
                new[]
                {
                    new Type[0],
                    new Type[0]
                });

            doSomethingMethodBuilder.DefineParameter(1, ParameterAttributes.In, "memory")
                // I pulled this from a decompiled assembly. You will get a signature doesnt match exception if you don't include it.
                .SetCustomAttribute(typeof(IsReadOnlyAttribute).GetConstructors()[0], new byte[] { 01, 00, 00, 00 });

            doSomethingMethodBuilder.DefineParameter(2, ParameterAttributes.None, "o");

            // Build method body
            var methodIlGenerator = doSomethingMethodBuilder.GetILGenerator();

            methodIlGenerator.Emit(OpCodes.Ldarg_0);
            methodIlGenerator.Emit(OpCodes.Ldfld, thisField);
            methodIlGenerator.Emit(OpCodes.Ldarg_1);
            methodIlGenerator.Emit(OpCodes.Ldarg_2);
            methodIlGenerator.Emit(OpCodes.Callvirt, concreteType.GetMethod("DoSomething"));

            methodIlGenerator.Emit(OpCodes.Ret);

            // Point the interfaces method to the overidden one.
            typeBuilder.DefineMethodOverride(doSomethingMethodBuilder, methodToOverride);

            // Create type and create an instance
            Type objectType = typeBuilder.CreateType();
            testInterfaceImpl = (ITestInterface<T>)Activator.CreateInstance(objectType, this);
        }
        
        /// <summary>
        /// Adding virtual to this breaks it.
        /// </summary>
        public virtual T DoSomething(in ReadOnlySpan<byte> memory, T o)
        {
            Console.WriteLine(memory[0]);
            Console.WriteLine(o);

            return new T();
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
                    null,
                    new[] { typeof(ITestInterface<T>) });
            return tb;
        }

    }

    public interface ITestInterface<T>
    {

        T DoSomething(in ReadOnlySpan<byte> memory, T o);

    }
    
}
