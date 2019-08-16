using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace DynamicMethodsLib
{
    public static class DynamicMethodFactory
    {
        private static DynamicMethod CreateDynamicMethod(string name, Type returnType, Type[] parameterTypes, Type owner)
        {
            //const MethodAttributes methodAttributes = MethodAttributes.Static | MethodAttributes.Public;

            DynamicMethod dynamicMethod = !owner.IsInterface
                ? new DynamicMethod(name, returnType, parameterTypes, owner, skipVisibility: true)
                : new DynamicMethod(name, returnType, parameterTypes, owner.Module, skipVisibility: true);

            return dynamicMethod;
        }

        public static Func<object, object[], object> CreateMethodCall(MethodInfo methodInfo, bool skipConvertion = false)
        {
            if (methodInfo != null)
            {
                DynamicMethod dynamicMethod = CreateDynamicMethod(
                    name: methodInfo.Name,
                    returnType: typeof(object),
                    parameterTypes: new Type[] { typeof(object), typeof(object[]) /* 2 параметра – инстанс и аргументы. */ },
                    owner: methodInfo.DeclaringType);

                //bool tr = dynamicMethod.IsSecurityTransparent;
                dynamicMethod.InitLocals = true;
                ILGenerator il = dynamicMethod.GetILGenerator();
                GenerateIL(methodInfo, il, 1, skipConvertion);

                var invokeDelagate = (Func<object, object[], object>)dynamicMethod.CreateDelegate(typeof(Func<object, object[], object>));

#if NET471 && DECOMPILE
                Decompiler.DecompileToConsoleAsync(methodInfo, skipConvertion).GetAwaiter().GetResult();
#endif

                return invokeDelagate;
            }
            else
                throw new ArgumentNullException(nameof(methodInfo));
        }

        public static void GenerateIL(MethodBase method, ILGenerator il, int argsIndex, bool skipConvertion)
        {
            ParameterInfo[] parameters = method.GetParameters();
            bool hasOutArgs = parameters.Any(x => x.ParameterType.IsByRef);

            // Проверка количества входных аргументов.
            //Label argsOk = il.DefineLabel();
            //il.Emit(OpCodes.Ldarg, argsIndex);
            //il.Emit(OpCodes.Ldlen);
            //il.Emit(OpCodes.Ldc_I4, args.Length);
            //il.Emit(OpCodes.Beq, argsOk);
            //il.Emit(OpCodes.Newobj, typeof(TargetParameterCountException).GetConstructor(Type.EmptyTypes));
            //il.Emit(OpCodes.Throw);
            //il.MarkLabel(argsOk);


            if (!method.IsConstructor && !method.IsStatic)
            {
                il.PushInstance(method.DeclaringType);
            }

            LocalBuilder localConvertible = null;
            if (!skipConvertion)
                localConvertible = il.DeclareLocal(typeof(IConvertible));
            
            LocalBuilder localObject = il.DeclareLocal(typeof(object));

            // Что-бы скопировать ref и out переменные обратно во входной массив object[] args.
            var outVarList = new List<OutRefArg>();

            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                Type parameterType = parameter.ParameterType;

                if(hasOutArgs)
                {
                    if (parameterType.IsByRef)
                    {
                        parameterType = parameterType.GetElementType();
                        LocalBuilder localVariable = il.DeclareLocal(parameterType);

                        outVarList.Add(new OutRefArg(parameter, i, localVariable));

                        // Для 'out' не нужно создавать переменную.
                        if (!parameter.IsOut)
                        {
                            il.PushArrayInstance(argsIndex, i);

                            if (parameterType.IsValueType)
                            {
                                Label skipSettingDefault = il.DefineLabel();
                                Label finishedProcessingParameter = il.DefineLabel();

                                // check if parameter is not null
                                il.Emit(OpCodes.Brtrue_S, skipSettingDefault);

                                // parameter has no value, initialize to default
                                il.Emit(OpCodes.Ldloca_S, localVariable);
                                il.Emit(OpCodes.Initobj, parameterType);
                                il.Emit(OpCodes.Br_S, finishedProcessingParameter);

                                // parameter has value, get value from array again and unbox and set to variable
                                il.MarkLabel(skipSettingDefault);
                                il.PushArrayInstance(argsIndex, i);
                                il.UnboxIfNeeded(parameterType);
                                il.Emit(OpCodes.Stloc_S, localVariable);

                                // parameter finished, we out!
                                il.MarkLabel(finishedProcessingParameter);
                            }
                            else
                            {
                                il.UnboxIfNeeded(parameterType);
                                il.Emit(OpCodes.Stloc_S, localVariable);
                            }
                        }

                        il.Emit(OpCodes.Ldloca_S, localVariable);
                    }
                    else if (parameterType.IsValueType && !skipConvertion)
                    {
                        il.PushArrayInstance(argsIndex, i);
                        il.Emit(OpCodes.Stloc_S, localObject);

                        // have to check that value type parameters aren't null
                        // otherwise they will error when unboxed
                        //Label skipSettingDefault = il.DefineLabel();
                        Label finishedProcessingParameter = il.DefineLabel();

                        // check if parameter is not null
                        //il.Emit(OpCodes.Ldloc_S, localObject);
                        //il.Emit(OpCodes.Brtrue_S, skipSettingDefault);

                        // parameter has no value, initialize to default
                        //LocalBuilder localVariable = il.DeclareLocal(parameterType);
                        //il.Emit(OpCodes.Ldloca_S, localVariable);
                        //il.Emit(OpCodes.Initobj, parameterType);
                        //il.Emit(OpCodes.Ldloc_S, localVariable);
                        //il.Emit(OpCodes.Br_S, finishedProcessingParameter);

                        // argument has value, try to convert it to parameter type
                        //il.MarkLabel(skipSettingDefault);

                        if (parameterType.IsPrimitive)
                        {
                            // for primitive types we need to handle type widening (e.g. short -> int)
                            MethodInfo toParameterTypeMethod = typeof(IConvertible)
                                .GetMethod("To" + parameterType.Name, new[] { typeof(IFormatProvider) });

                            if (toParameterTypeMethod != null)
                            {
                                Label skipConvertible = il.DefineLabel();

                                // check if argument type is an exact match for parameter type
                                // in this case we may use cheap unboxing instead
                                il.Emit(OpCodes.Ldloc_S, localObject);
                                il.Emit(OpCodes.Isinst, parameterType);
                                il.Emit(OpCodes.Brtrue_S, skipConvertible);

                                // types don't match, check if argument implements IConvertible
                                il.Emit(OpCodes.Ldloc_S, localObject);
                                il.Emit(OpCodes.Isinst, typeof(IConvertible));
                                il.Emit(OpCodes.Stloc_S, localConvertible);
                                il.Emit(OpCodes.Ldloc_S, localConvertible);
                                il.Emit(OpCodes.Brfalse_S, skipConvertible);

                                // convert argument to parameter type
                                il.Emit(OpCodes.Ldloc_S, localConvertible);
                                il.Emit(OpCodes.Ldnull);
                                il.Emit(OpCodes.Callvirt, toParameterTypeMethod);
                                il.Emit(OpCodes.Br_S, finishedProcessingParameter);

                                il.MarkLabel(skipConvertible);
                            }
                        }

                        // we got here because either argument type matches parameter (conversion will succeed),
                        // or argument type doesn't match parameter, but we're out of options (conversion will fail)
                        il.Emit(OpCodes.Ldloc_S, localObject);

                        il.UnboxIfNeeded(parameterType);

                        // parameter finished, we out!
                        il.MarkLabel(finishedProcessingParameter);
                    }
                    else
                    {
                        il.PushArrayInstance(argsIndex, i);
                        il.UnboxIfNeeded(parameterType);
                    }
                }
                else
                {
                    if (parameterType.IsByRef)
                    {
                        parameterType = parameterType.GetElementType();
                        LocalBuilder localVariable = il.DeclareLocal(parameterType);

                        outVarList.Add(new OutRefArg(parameter, i, localVariable));

                        // Для 'out' не нужно создавать переменную.
                        if (!parameter.IsOut)
                        {
                            il.PushArrayInstance(argsIndex, i);

                            if (parameterType.IsValueType)
                            {
                                Label skipSettingDefault = il.DefineLabel();
                                Label finishedProcessingParameter = il.DefineLabel();

                                // check if parameter is not null
                                il.Emit(OpCodes.Brtrue_S, skipSettingDefault);

                                // parameter has no value, initialize to default
                                il.Emit(OpCodes.Ldloca_S, localVariable);
                                il.Emit(OpCodes.Initobj, parameterType);
                                il.Emit(OpCodes.Br_S, finishedProcessingParameter);

                                // parameter has value, get value from array again and unbox and set to variable
                                il.MarkLabel(skipSettingDefault);
                                il.PushArrayInstance(argsIndex, i);
                                il.UnboxIfNeeded(parameterType);
                                il.Emit(OpCodes.Stloc_S, localVariable);

                                // parameter finished, we out!
                                il.MarkLabel(finishedProcessingParameter);
                            }
                            else
                            {
                                il.UnboxIfNeeded(parameterType);
                                il.Emit(OpCodes.Stloc_S, localVariable);
                            }
                        }

                        il.Emit(OpCodes.Ldloca_S, localVariable);
                    }
                    else if (parameterType.IsValueType && !skipConvertion)
                    {
                        il.PushArrayInstance(argsIndex, i);
                        il.Emit(OpCodes.Stloc_S, localObject);

                        // have to check that value type parameters aren't null
                        // otherwise they will error when unboxed
                        //Label skipSettingDefault = il.DefineLabel();
                        Label finishedProcessingParameter = il.DefineLabel();

                        // check if parameter is not null
                        //il.Emit(OpCodes.Ldloc_S, localObject);
                        //il.Emit(OpCodes.Brtrue_S, skipSettingDefault);

                        // parameter has no value, initialize to default
                        //LocalBuilder localVariable = il.DeclareLocal(parameterType);
                        //il.Emit(OpCodes.Ldloca_S, localVariable);
                        //il.Emit(OpCodes.Initobj, parameterType);
                        //il.Emit(OpCodes.Ldloc_S, localVariable);
                        //il.Emit(OpCodes.Br_S, finishedProcessingParameter);

                        // argument has value, try to convert it to parameter type
                        //il.MarkLabel(skipSettingDefault);

                        if (parameterType.IsPrimitive)
                        {
                            // for primitive types we need to handle type widening (e.g. short -> int)
                            MethodInfo toParameterTypeMethod = typeof(IConvertible)
                                .GetMethod("To" + parameterType.Name, new[] { typeof(IFormatProvider) });

                            if (toParameterTypeMethod != null)
                            {
                                Label skipConvertible = il.DefineLabel();

                                // check if argument type is an exact match for parameter type
                                // in this case we may use cheap unboxing instead
                                il.Emit(OpCodes.Ldloc_S, localObject);
                                il.Emit(OpCodes.Isinst, parameterType);
                                il.Emit(OpCodes.Brtrue_S, skipConvertible);

                                // types don't match, check if argument implements IConvertible
                                il.Emit(OpCodes.Ldloc_S, localObject);
                                il.Emit(OpCodes.Isinst, typeof(IConvertible));
                                il.Emit(OpCodes.Stloc_S, localConvertible);
                                il.Emit(OpCodes.Ldloc_S, localConvertible);
                                il.Emit(OpCodes.Brfalse_S, skipConvertible);

                                // convert argument to parameter type
                                il.Emit(OpCodes.Ldloc_S, localConvertible);
                                il.Emit(OpCodes.Ldnull);
                                il.Emit(OpCodes.Callvirt, toParameterTypeMethod);
                                il.Emit(OpCodes.Br_S, finishedProcessingParameter);

                                il.MarkLabel(skipConvertible);
                            }
                        }

                        // we got here because either argument type matches parameter (conversion will succeed),
                        // or argument type doesn't match parameter, but we're out of options (conversion will fail)
                        il.Emit(OpCodes.Ldloc_S, localObject);

                        il.UnboxIfNeeded(parameterType);

                        // parameter finished, we out!
                        il.MarkLabel(finishedProcessingParameter);
                    }
                    else
                    {
                        il.PushArrayInstance(argsIndex, i);
                        il.UnboxIfNeeded(parameterType);
                    }
                }
            }

            if (method.IsConstructor)
                il.Emit(OpCodes.Newobj, (ConstructorInfo)method);
            else
            {
                var methodInfo = (MethodInfo)method;
                il.CallMethod(methodInfo);
            }

            if (hasOutArgs)
            {
                #region Копирование Out и Ref параметров

                foreach (var outVar in outVarList)
                {
                    // Загрузить args в стек.
                    il.Emit(OpCodes.Ldarg_1);
                    // Индекс массива args.
                    il.Emit_Ldc_I4(outVar.Index);
                    // Загрузить локальную переменную.
                    il.Emit(OpCodes.Ldloc_S, outVar.Local);

                    // ref Type => Type (System.Int32& => System.Int32).
                    Type type = outVar.Param.ParameterType.GetElementType();
                    if (type.IsValueType)
                        il.Emit(OpCodes.Box, type);

                    // Записать локальную переменную в массив.
                    il.Emit(OpCodes.Stelem_Ref);
                }
                #endregion
            }

            Type returnType = method.IsConstructor ? method.DeclaringType : ((MethodInfo)method).ReturnType;
            if (returnType != typeof(void))
            {
                if (returnType.IsValueType)
                    il.Emit(OpCodes.Box, returnType);
                else
                {
                    // Не нужно кастовать если возвращаемый тип совпадает.
                    if (returnType != typeof(object))
                        il.Emit(OpCodes.Castclass, returnType);
                }
            }
            else
            {
                // Если функция возвращает void то вернём null.
                il.Emit(OpCodes.Ldnull);
            }
            il.Emit(OpCodes.Ret);
        }

        private static void GenerateCreateSetPropertyIL(PropertyInfo propertyInfo, ILGenerator generator)
        {
            MethodInfo setMethod = propertyInfo.GetSetMethod(true);
            if (!setMethod.IsStatic)
            {
                generator.PushInstance(propertyInfo.DeclaringType);
            }

            generator.Emit(OpCodes.Ldarg_1);
            generator.UnboxIfNeeded(propertyInfo.PropertyType);
            generator.CallMethod(setMethod);
            generator.Return();
        }

        private static void GenerateCreateSetFieldIL(FieldInfo fieldInfo, ILGenerator generator)
        {
            if (!fieldInfo.IsStatic)
            {
                generator.PushInstance(fieldInfo.DeclaringType);
            }

            generator.Emit(OpCodes.Ldarg_1);
            generator.UnboxIfNeeded(fieldInfo.FieldType);

            if (!fieldInfo.IsStatic)
            {
                generator.Emit(OpCodes.Stfld, fieldInfo);
            }
            else
            {
                generator.Emit(OpCodes.Stsfld, fieldInfo);
            }

            generator.Return();
        }
    }
}