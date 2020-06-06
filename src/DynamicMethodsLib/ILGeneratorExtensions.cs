using System;
using System.Reflection;
using System.Reflection.Emit;

namespace DynamicMethodsLib
{
    internal static class ILGeneratorExtensions
    {
        /// <summary>
        /// Ldarg_0.
        /// </summary>
        public static void PushInstance(this ILGenerator generator, Type type)
        {
            generator.Emit(OpCodes.Ldarg_0);
            if (type.IsValueType)
            {
                generator.Emit(OpCodes.Unbox, type);
            }
            else
            {
                generator.Emit(OpCodes.Castclass, type);
            }
        }

        public static void PushArrayInstance(this ILGenerator generator, int argsIndex, int arrayIndex)
        {
            Emit_Ldarg(generator, argsIndex);
            Emit_Ldc_I4(generator, arrayIndex);
            generator.Emit(OpCodes.Ldelem_Ref);
        }

        /// <summary>
        /// Загружает аргумент с индексом <paramref name="argIndex"/> в стек вычислений.
        /// </summary>
        public static void Emit_Ldarg(this ILGenerator generator, int argIndex)
        {
            switch (argIndex)
            {
                case 0:
                    generator.Emit(OpCodes.Ldarg_0);
                    break;
                case 1:
                    generator.Emit(OpCodes.Ldarg_1);
                    break;
                case 2:
                    generator.Emit(OpCodes.Ldarg_2);
                    break;
                case 3:
                    generator.Emit(OpCodes.Ldarg_3);
                    break;
                default:
                    generator.Emit((argIndex < 128 ? OpCodes.Ldarg_S : OpCodes.Ldarg), argIndex);
                    break;
            }
        }

        /// <summary>
        /// Помещает целочисленное значение <paramref name="n"/> в стек вычислений как <see langword="int32"/>.
        /// </summary>
        public static void Emit_Ldc_I4(this ILGenerator generator, int n)
        {
            switch (n)
            {
                case 0:
                    generator.Emit(OpCodes.Ldc_I4_0);
                    break;
                case 1:
                    generator.Emit(OpCodes.Ldc_I4_1);
                    break;
                case 2:
                    generator.Emit(OpCodes.Ldc_I4_2);
                    break;
                case 3:
                    generator.Emit(OpCodes.Ldc_I4_3);
                    break;
                case 4:
                    generator.Emit(OpCodes.Ldc_I4_4);
                    break;
                case 5:
                    generator.Emit(OpCodes.Ldc_I4_5);
                    break;
                case 6:
                    generator.Emit(OpCodes.Ldc_I4_6);
                    break;
                case 7:
                    generator.Emit(OpCodes.Ldc_I4_7);
                    break;
                case 8:
                    generator.Emit(OpCodes.Ldc_I4_8);
                    break;
                default:
                    generator.Emit((n < 128 ? OpCodes.Ldc_I4_S : OpCodes.Ldc_I4), n);
                    break;
            }
        }

        public static void BoxIfNeeded(this ILGenerator generator, Type type)
        {
            if (type.IsValueType)
            {
                generator.Emit(OpCodes.Box, type);
            }
            else
            {
                generator.Emit(OpCodes.Castclass, type);
            }
        }

        public static void UnboxIfNeeded(this ILGenerator generator, Type type)
        {
            if (type.IsValueType)
            {
                generator.Emit(OpCodes.Unbox_Any, type);
            }
            else
            {
                generator.Emit(OpCodes.Castclass, type);
            }
        }

        public static void CallMethod(this ILGenerator generator, MethodInfo methodInfo)
        {
            if (methodInfo.IsFinal || !methodInfo.IsVirtual)
            {
                generator.Emit(OpCodes.Call, methodInfo);
            }
            else
            {
                generator.Emit(OpCodes.Callvirt, methodInfo);
            }
        }

        public static void Return(this ILGenerator generator)
        {
            generator.Emit(OpCodes.Ret);
        }
    }
}
