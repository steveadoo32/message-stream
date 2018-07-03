using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace MessageStream.Serializer
{

    /// <summary>
    /// Deserializes bytes in stages. Useful if your messages have fixed size headers and variable bodies.
    /// </summary>
    public abstract class StagedDeserializer<T> : IDeserializer<T>
    {

        private IDeserializationPipeline<T> pipeline;

        public abstract int HeaderLength { get; }

        public StagedDeserializer()
        {
            Initialize();
        }

        private void Initialize()
        {
            Type concreteType = GetType();
            Type pipelineType = typeof(IDeserializationPipeline<T>);

            var methodToOverride = pipelineType.GetMethod(nameof(IDeserializationPipeline<T>.Deserialize));
            string overrideMethodName = string.Format("{0}.{1}", pipelineType.FullName, methodToOverride.Name);

            var typeBuilder = CreateTypeBuilderForDeserializer(GetType().Name);

            var deserializerField = typeBuilder.DefineField("deserializer", concreteType, FieldAttributes.Private);

            var constructor = typeBuilder.DefineConstructor(
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, CallingConventions.HasThis, new[] { concreteType });

            var constructorIlGenerator = constructor.GetILGenerator();

            constructorIlGenerator.Emit(OpCodes.Ldarg_0);
            constructorIlGenerator.Emit(OpCodes.Ldarg_1);
            constructorIlGenerator.Emit(OpCodes.Stfld, deserializerField);            
            constructorIlGenerator.Emit(OpCodes.Ret);

            var deserializeMethodBuilder = typeBuilder.DefineMethod(
                overrideMethodName,
                MethodAttributes.Public  | MethodAttributes.HideBySig | 
                MethodAttributes.Virtual | MethodAttributes.Final,
                CallingConventions.HasThis,
                typeof(bool),
                new Type[0],
                new Type[0],
                new[] 
                {
                    typeof(ReadOnlySequence<byte>).MakeByRefType(),
                    typeof(SequencePosition).MakeByRefType(),
                    typeof(T).MakeByRefType()
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

            deserializeMethodBuilder
                .DefineParameter(2, ParameterAttributes.Out, "position");
            deserializeMethodBuilder
                .DefineParameter(3, ParameterAttributes.Out, "message");

            // Build method body
            var methodIlGenerator = deserializeMethodBuilder.GetILGenerator();

            // Gather the stages
            var stageMethods = GetType()
                .GetRuntimeMethods()
                .Where(method => method.GetCustomAttribute(typeof(DeserializationStage)) != null)
                .Select(method => new
                {
                    method = method,
                    attribute = (DeserializationStage)method.GetCustomAttribute(typeof(DeserializationStage))
                })
                .OrderBy(tuple => tuple.attribute.Stage)
                .Select(tuple => tuple.method)
                .ToArray();

            // Define local vars
            var initialPositionLocal = methodIlGenerator.DeclareLocal(typeof(SequencePosition));
            var bufferReaderLocal = methodIlGenerator.DeclareLocal(typeof(BufferReader));
            var stageBufferBytesLocal = methodIlGenerator.DeclareLocal(typeof(byte*));
            var stageBufferLocal = methodIlGenerator.DeclareLocal(typeof(Span<byte>));
            var readOnlyStageBufferLocal = methodIlGenerator.DeclareLocal(typeof(ReadOnlySpan<byte>));
            var stageLengthLocal = methodIlGenerator.DeclareLocal(typeof(int));
            var lastStageLengthLocal = methodIlGenerator.DeclareLocal(typeof(int));

            // Have to declare these first too before the rest of the method.
            var stageMethodLocals = stageMethods.Select(sm => methodIlGenerator.DeclareLocal(sm.ReturnType)).ToArray();
            
            // position = buffer.Start;
            methodIlGenerator.Emit(OpCodes.Ldarg_2);
            methodIlGenerator.Emit(OpCodes.Ldarg_1);
            methodIlGenerator.Emit(OpCodes.Call, typeof(ReadOnlySequence<byte>).GetProperty("Start").GetMethod);
            methodIlGenerator.Emit(OpCodes.Stobj, typeof(SequencePosition));

            // message = default;
            if (typeof(T).IsValueType)
            {
                methodIlGenerator.Emit(OpCodes.Ldarg_3);
                methodIlGenerator.Emit(OpCodes.Initobj, typeof(T));
            }
            else
            {
                methodIlGenerator.Emit(OpCodes.Ldarg_3);
                methodIlGenerator.Emit(OpCodes.Ldnull);
                methodIlGenerator.Emit(OpCodes.Stind_Ref);
            }
            
            // Initialize the locals for each stage
            foreach (var stageLocal in stageMethodLocals)
            {
                if (stageLocal.LocalType.IsValueType)
                {
                    methodIlGenerator.Emit(OpCodes.Ldloca, stageLocal);
                    methodIlGenerator.Emit(OpCodes.Initobj, stageLocal.LocalType);
                }
                else
                {
                    methodIlGenerator.Emit(OpCodes.Ldnull);
                    methodIlGenerator.Emit(OpCodes.Stloc, stageLocal);
                }
            }

            //readOnlyStageBuffer = default;
            methodIlGenerator.Emit(OpCodes.Ldloca, readOnlyStageBufferLocal);
            methodIlGenerator.Emit(OpCodes.Initobj, readOnlyStageBufferLocal.LocalType);

            //stageBuffer = default;
            methodIlGenerator.Emit(OpCodes.Ldloca, stageBufferLocal);
            methodIlGenerator.Emit(OpCodes.Initobj, stageBufferLocal.LocalType);

            // stageLength = HeaderLength;
            methodIlGenerator.Emit(OpCodes.Ldc_I4, HeaderLength);
            methodIlGenerator.Emit(OpCodes.Stloc, stageLengthLocal);

            // lastStageLength = 0
            methodIlGenerator.Emit(OpCodes.Ldc_I4_0);
            methodIlGenerator.Emit(OpCodes.Stloc, lastStageLengthLocal);

            //bufferReader = new BufferReader(buffer);
            methodIlGenerator.Emit(OpCodes.Ldarg_1);
            methodIlGenerator.Emit(OpCodes.Newobj, bufferReaderLocal.LocalType.GetConstructors()[0]);
            methodIlGenerator.Emit(OpCodes.Stloc, bufferReaderLocal);
            
            MethodInfo bufferReadMethod = typeof(BufferReaderExtensions).GetMethod(nameof(BufferReaderExtensions.ReadBytes));
            ConstructorInfo spanCtor = typeof(ReadOnlySpan<byte>).GetConstructor(new[] { typeof(void*), typeof(int) });
            ConstructorInfo readOnlySpanCtor = typeof(ReadOnlySpan<byte>).GetConstructor(new[] { typeof(void*), typeof(int) });

            LocalBuilder previousStageResultLocal = null;
            LocalBuilder stageResultLocal = null;
            Label nextBranchLabel = default;

            // Emit calls to stages
            for (int i = 0; i < stageMethods.Length; i++)
            {
                bool first = i == 0;
                bool last = i == stageMethods.Length - 1;
                var method = stageMethods[i];
                
                // Emit bounds check.
                nextBranchLabel = EmitBoundsCheck(methodIlGenerator, 1, 2, bufferReaderLocal, stageLengthLocal, initialPositionLocal);

                methodIlGenerator.MarkLabel(nextBranchLabel);

                // Mark the last stage result so we can pass it to this stage
                previousStageResultLocal = stageResultLocal;

                // Declare result for this stage
                stageResultLocal = stageMethodLocals[i];

                // Initialize memory on stack (if needed. if the old buffer is large enough we reuse it.)
                var skipBufferAllocationLabel = methodIlGenerator.DefineLabel();
                
                // if (lastStageLength - stageLength < 0) { //allocate }
                methodIlGenerator.Emit(OpCodes.Ldloc, lastStageLengthLocal);
                methodIlGenerator.Emit(OpCodes.Ldloc, stageLengthLocal);
                methodIlGenerator.Emit(OpCodes.Sub);
                methodIlGenerator.Emit(OpCodes.Ldc_I4_0);
                methodIlGenerator.Emit(OpCodes.Bge, skipBufferAllocationLabel);
                
                // Allocate if needed. stageBufferBytes = stackalloc byte[stageLength];
                // TODO if the message size is larger than the stack this will throw an exception. Should we support larger messages?
                methodIlGenerator.Emit(OpCodes.Ldloc, stageLengthLocal);
                methodIlGenerator.Emit(OpCodes.Conv_U);
                methodIlGenerator.Emit(OpCodes.Localloc);
                methodIlGenerator.Emit(OpCodes.Stloc, stageBufferBytesLocal);
                
                methodIlGenerator.MarkLabel(skipBufferAllocationLabel);

                // Wrap memory into a span
                methodIlGenerator.Emit(OpCodes.Ldloca, stageBufferLocal);
                methodIlGenerator.Emit(OpCodes.Ldloc, stageBufferBytesLocal);
                methodIlGenerator.Emit(OpCodes.Ldloc, stageLengthLocal);
                methodIlGenerator.Emit(OpCodes.Call, spanCtor);

                // Read from buffer reader into memory
                methodIlGenerator.Emit(OpCodes.Ldloca, bufferReaderLocal);
                methodIlGenerator.Emit(OpCodes.Ldarg_2);
                methodIlGenerator.Emit(OpCodes.Ldloc, stageBufferLocal);
                methodIlGenerator.Emit(OpCodes.Call, bufferReadMethod);
                
                // Wrap bytes in ReadOnlySpan<byte>
                methodIlGenerator.Emit(OpCodes.Ldloca, readOnlyStageBufferLocal);
                methodIlGenerator.Emit(OpCodes.Ldloc, stageBufferBytesLocal);
                methodIlGenerator.Emit(OpCodes.Ldloc, stageLengthLocal);
                methodIlGenerator.Emit(OpCodes.Call, readOnlySpanCtor);

                methodIlGenerator.Emit(OpCodes.Ldloc, stageLengthLocal);
                methodIlGenerator.Emit(OpCodes.Stloc, lastStageLengthLocal);

                // Call the stage method. If first iteration we have no state to pass in
                // If its the last iteration we don't need to pass in stageLength
                if (first)
                {
                    methodIlGenerator.Emit(OpCodes.Ldarg_0);
                    methodIlGenerator.Emit(OpCodes.Ldfld, deserializerField);
                    methodIlGenerator.Emit(OpCodes.Ldloca, readOnlyStageBufferLocal);
                    methodIlGenerator.Emit(OpCodes.Ldloca, stageLengthLocal);
                    methodIlGenerator.Emit(OpCodes.Call, method);
                    methodIlGenerator.Emit(OpCodes.Stloc, stageResultLocal);
                }
                else if (last)
                {
                    methodIlGenerator.Emit(OpCodes.Ldarg_0);
                    methodIlGenerator.Emit(OpCodes.Ldfld, deserializerField);
                    methodIlGenerator.Emit(OpCodes.Ldloca, readOnlyStageBufferLocal);
                    methodIlGenerator.Emit(OpCodes.Ldloc, previousStageResultLocal);
                    methodIlGenerator.Emit(OpCodes.Call, method);
                    methodIlGenerator.Emit(OpCodes.Stloc, stageResultLocal);
                }
                else
                {
                    methodIlGenerator.Emit(OpCodes.Ldarg_0);
                    methodIlGenerator.Emit(OpCodes.Ldfld, deserializerField);
                    methodIlGenerator.Emit(OpCodes.Ldloca, readOnlyStageBufferLocal);
                    methodIlGenerator.Emit(OpCodes.Ldloc, previousStageResultLocal);
                    methodIlGenerator.Emit(OpCodes.Ldloca, stageLengthLocal);
                    methodIlGenerator.Emit(OpCodes.Call, method);
                    methodIlGenerator.Emit(OpCodes.Stloc, stageResultLocal);
                }
            }

            // Set the message to the last stage result, which should be the message type they're expecting.
            methodIlGenerator.Emit(OpCodes.Ldarg_3);
            methodIlGenerator.Emit(OpCodes.Ldloc, stageResultLocal);

            if (stageResultLocal.LocalType.IsValueType)
            {
                methodIlGenerator.Emit(OpCodes.Stobj, stageResultLocal.LocalType);
            }
            else
            {
                methodIlGenerator.Emit(OpCodes.Stind_Ref);
            }

            // Set position to the current buffer position.
            methodIlGenerator.Emit(OpCodes.Ldarg_2);
            methodIlGenerator.Emit(OpCodes.Ldloca, bufferReaderLocal);
            methodIlGenerator.Emit(OpCodes.Call, typeof(BufferReader).GetProperty("Position").GetMethod);
            methodIlGenerator.Emit(OpCodes.Stobj, typeof(SequencePosition));

            // return true;
            methodIlGenerator.Emit(OpCodes.Ldc_I4_1);
            methodIlGenerator.Emit(OpCodes.Ret);

            // Point the interfaces method to the overidden one.
            typeBuilder.DefineMethodOverride(deserializeMethodBuilder, methodToOverride);

            // Create type and create an instance
            Type objectType = typeBuilder.CreateType();            
            pipeline = (IDeserializationPipeline<T>) Activator.CreateInstance(objectType, this);
        }

        private Label EmitBoundsCheck(ILGenerator ilGenerator, int bufferParamIndex, int positionParamIndex, LocalBuilder bufferReaderLocal, LocalBuilder stageLengthLocal, LocalBuilder initialPositionLocal)
        {
            var label = ilGenerator.DefineLabel();
            
            // Make sure we have enough data to read.
            ilGenerator.Emit(OpCodes.Ldarg, bufferParamIndex);
            ilGenerator.Emit(OpCodes.Call, typeof(ReadOnlySequence<byte>).GetProperty("Length").GetMethod);
            ilGenerator.Emit(OpCodes.Ldloca, bufferReaderLocal);
            ilGenerator.Emit(OpCodes.Call, bufferReaderLocal.LocalType.GetProperty("ConsumedBytes").GetMethod);
            ilGenerator.Emit(OpCodes.Conv_I8);
            ilGenerator.Emit(OpCodes.Sub);
            ilGenerator.Emit(OpCodes.Ldloc, stageLengthLocal);
            ilGenerator.Emit(OpCodes.Conv_I8);
            ilGenerator.Emit(OpCodes.Sub);
            ilGenerator.Emit(OpCodes.Ldc_I4_0);
            ilGenerator.Emit(OpCodes.Conv_I8);
            ilGenerator.Emit(OpCodes.Bge, label);

            // position = initialPosition;
            ilGenerator.Emit(OpCodes.Ldarg, positionParamIndex);
            ilGenerator.Emit(OpCodes.Ldloc, initialPositionLocal);
            ilGenerator.Emit(OpCodes.Stobj, typeof(SequencePosition));

            // return false;
            ilGenerator.Emit(OpCodes.Ldc_I4_0);
            ilGenerator.Emit(OpCodes.Ret);

            return label;
        }

        bool IDeserializer<T>.Deserialize(in ReadOnlySequence<byte> buffer, out SequencePosition read, out T message)
        {
            return pipeline.Deserialize(in buffer, out read, out message);
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
                    new [] { typeof(IDeserializationPipeline<T>) });
            return tb;
        }
        
    }

    public interface IDeserializationPipeline<T>
    {

        bool Deserialize(in ReadOnlySequence<byte> buffer, out SequencePosition read, out T message);

    }
    
    public class DeserializationStage : Attribute
    {

        public int Stage { get; }

        public DeserializationStage(int stage)
        {
            Stage = stage;
        }

    }

}
