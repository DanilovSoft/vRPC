using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using DanilovSoft.vRPC;

namespace DynamicMethodsLib
{
    internal static class ProxyBuilder<TClass>
    {
        private const BindingFlags _visibilityFlags = BindingFlags.Public | BindingFlags.Instance;
        private static readonly MethodInfo _invokeMethod = typeof(TClass).GetMethod("Invoke",
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(MethodInfo), typeof(object[]) },
                modifiers: null)!;
        private static readonly MethodInfo _noresultInvokeMethod = typeof(TClass).GetMethod("NoResultInvoke",
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(MethodInfo), typeof(object[]) },
                modifiers: null)!;
        private static readonly MethodInfo _taskInvokeMethod = typeof(TClass).GetMethod("TaskInvoke",
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(MethodInfo), typeof(object[]) },
                modifiers: null)!;
        private static readonly MethodInfo _emptyTaskInvokeMethod = typeof(TClass).GetMethod("EmptyTaskInvoke",
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(MethodInfo), typeof(object[]) },
                modifiers: null)!;
        private static readonly MethodInfo _valueTaskInvokeMethod = typeof(TClass).GetMethod("ValueTaskInvoke",
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(MethodInfo), typeof(object[]) },
                modifiers: null)!;
        private static readonly MethodInfo _emptyValueTaskInvokeMethod = typeof(TClass).GetMethod("EmptyValueTaskInvoke",
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(MethodInfo), typeof(object[]) },
                modifiers: null)!;

        static ProxyBuilder()
        {
            Debug.Assert(_invokeMethod != null, $"У типа {typeof(TClass).FullName} должен быть метод Invoke(MethodInfo m, object[] args)");
            Debug.Assert(_taskInvokeMethod != null, $"У типа {typeof(TClass).FullName} должен быть метод TaskInvoke<T>(MethodInfo m, object[] args)");
            Debug.Assert(_emptyTaskInvokeMethod != null, $"У типа {typeof(TClass).FullName} должен быть метод EmptyTaskInvoke(MethodInfo m, object[] args)");
            Debug.Assert(_valueTaskInvokeMethod != null, $"У типа {typeof(TClass).FullName} должен быть метод ValueTaskInvoke<T>(MethodInfo m, object[] args)");
            Debug.Assert(_noresultInvokeMethod != null, $"У типа {typeof(TClass).FullName} должен быть метод NoResultInvoke(MethodInfo m, object[] args)");
            Debug.Assert(_emptyValueTaskInvokeMethod != null, $"У типа {typeof(TClass).FullName} должен быть метод EmptyValueTaskInvoke(MethodInfo m, object[] args)");
        }

        private static AssemblyBuilder DefineDynamicAssembly(string name)
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);
            return assembly;
        }

        private static ModuleBuilder DefineDynamicModule(AssemblyBuilder assembly)
        {
            return assembly.DefineDynamicModule("Module");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TIface"></typeparam>
        /// <param name="source"></param>
        /// <param name="instance">Параметр который будет передан в конструктор <typeparamref name="TClass"/></param>
        /// <exception cref="VRpcException"/>
        public static TClass CreateProxy<TIface>(TClass source = default, object? instance = null)
        {
            var ifaceType = typeof(TIface);
            if (!ifaceType.IsPublic)
            {
                throw new VRpcException($"Интерфейс {ifaceType.FullName} должен быть публичным и должен быть видимым для других сборок.");
            }
            Debug.Assert(ifaceType.IsInterface, "Ожидался интерфейс");

            var proxyParentClassType = typeof(TClass);
            if(proxyParentClassType.IsSealed)
                throw new VRpcException($"Родительский класс {proxyParentClassType.FullName} не должен быть запечатанным.");

            //var genericProxyParentClass = proxyParentClassType.MakeGenericType(typeof(TIface));

            // Динамическая сборка в которой будут жить классы реализующие пользовательские интерфейсы.
            string assemblyName = ifaceType.Name + "_" + Guid.NewGuid().ToString();
            AssemblyBuilder assemblyBuilder = DefineDynamicAssembly(assemblyName);
            ModuleBuilder moduleBuilder = DefineDynamicModule(assemblyBuilder);
            string className = proxyParentClassType.Name + "_" + ifaceType.Name;
            TypeBuilder classType = moduleBuilder.DefineType(className, 
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, parent: proxyParentClassType);            

            classType.AddInterfaceImplementation(ifaceType);

            if (instance != null)
            // В конструктор должен передаваться инстанс.
            {
                Type instanceType = instance.GetType();

                // Пустой конструктор базового типа.
                ConstructorInfo? baseDefaultCtor = proxyParentClassType.GetConstructor(Type.EmptyTypes);

                // Базовый конструктор с параметром.
                ConstructorInfo? baseCtor = null;
                var baseCtors = proxyParentClassType.GetConstructors();
                foreach (ConstructorInfo ctor in baseCtors)
                {
                    var parameters = ctor.GetParameters();
                    if (parameters.Length == 1)
                    {
                        if (instanceType.IsAssignableFrom(parameters[0].ParameterType))
                        {
                            baseCtor = ctor;
                            break;
                        }
                    }
                }
                if (baseCtor == null)
                    throw new VRpcException($"Не найден конструктор принимающий один параметр типа {typeof(TIface).FullName}.");

                // Конструктор наследника с параметром.
                ConstructorBuilder constructor = classType.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, parameterTypes: new[] { instanceType });

                // Generate constructor code
                ILGenerator ilGenerator = constructor.GetILGenerator();
                ilGenerator.DeclareLocal(typeof(TIface));
                ilGenerator.Emit(OpCodes.Ldarg_0);              // push this onto stack.
                ilGenerator.Emit(OpCodes.Ldarg_1);
                ilGenerator.Emit(OpCodes.Call, baseCtor);       // call base constructor
                ilGenerator.Emit(OpCodes.Ret);                  // Return
            }
            else
            // Должен быть публичный и пустой конструктор.
            {
                ConstructorInfo? baseDefaultCtor = proxyParentClassType.GetConstructor(Type.EmptyTypes);
                if (baseDefaultCtor == null)
                    throw new VRpcException($"У типа {proxyParentClassType.FullName} должен быть пустой и открытый конструктор.");
            }

            int methodCount = 0;
            var methodsDict = new Dictionary<int, MethodInfo>();
            MethodInfo[] methods = ifaceType.GetMethods();
            var fields = new List<FieldMethodInfo>(methods.Length);

            var fieldsList = new List<string>();
            foreach (PropertyInfo v in ifaceType.GetProperties())
            {
                fieldsList.Add(v.Name);

                var field = classType.DefineField("_" + v.Name.ToUpperInvariant(), v.PropertyType, FieldAttributes.Private);
                var property = classType.DefineProperty(v.Name, PropertyAttributes.None, v.PropertyType, Array.Empty<Type>());
                var getter = classType.DefineMethod("get_" + v.Name, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.Virtual, v.PropertyType, Array.Empty<Type>());
                var setter = classType.DefineMethod("set_" + v.Name, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.Virtual, null, new Type[] { v.PropertyType });

                var getGenerator = getter.GetILGenerator();
                var setGenerator = setter.GetILGenerator();

                getGenerator.Emit(OpCodes.Ldarg_0);
                getGenerator.Emit(OpCodes.Ldfld, field);
                getGenerator.Emit(OpCodes.Ret);

                setGenerator.Emit(OpCodes.Ldarg_0);
                setGenerator.Emit(OpCodes.Ldarg_1);
                setGenerator.Emit(OpCodes.Stfld, field);
                setGenerator.Emit(OpCodes.Ret);

                property.SetGetMethod(getter);
                property.SetSetMethod(setter);

                MethodInfo? getMethod = v.GetGetMethod();
                Debug.Assert(getMethod != null);

                MethodInfo? setMethod = v.GetSetMethod();
                Debug.Assert(setMethod != null);

                classType.DefineMethodOverride(getter, getMethod);
                classType.DefineMethodOverride(setter, setMethod);
            }

            if (source != null)
            {
                foreach (PropertyInfo v in source.GetType().GetProperties())
                {
                    if (fieldsList.Contains(v.Name))
                        continue;

                    fieldsList.Add(v.Name);

                    var field = classType.DefineField("_" + v.Name.ToUpperInvariant(), v.PropertyType, FieldAttributes.Private);

                    var property = classType.DefineProperty(v.Name, PropertyAttributes.None, v.PropertyType, Array.Empty<Type>());
                    var getter = classType.DefineMethod("get_" + v.Name, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.Virtual, v.PropertyType, Array.Empty<Type>());
                    var setter = classType.DefineMethod("set_" + v.Name, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.Virtual, null, new Type[] { v.PropertyType });

                    var getGenerator = getter.GetILGenerator();
                    var setGenerator = setter.GetILGenerator();

                    getGenerator.Emit(OpCodes.Ldarg_0);
                    getGenerator.Emit(OpCodes.Ldfld, field);
                    getGenerator.Emit(OpCodes.Ret);

                    setGenerator.Emit(OpCodes.Ldarg_0);
                    setGenerator.Emit(OpCodes.Ldarg_1);
                    setGenerator.Emit(OpCodes.Stfld, field);
                    setGenerator.Emit(OpCodes.Ret);

                    property.SetGetMethod(getter);
                    property.SetSetMethod(setter);
                }
            }

            foreach (MethodInfo ifaceMethod in ifaceType.GetMethods())
            {
                //    const MethodAttributes ExplicitImplementation =
                //MethodAttributes.Private |
                //MethodAttributes.Final |
                //MethodAttributes.Virtual |
                //MethodAttributes.HideBySig |
                //MethodAttributes.NewSlot;

                const MethodAttributes ImplicitImplementation =
                    MethodAttributes.Public |
                    MethodAttributes.Final |
                    MethodAttributes.Virtual |
                    MethodAttributes.HideBySig |
                    MethodAttributes.NewSlot;

                if (!ifaceMethod.IsSpecialName)
                {
                    if (ifaceMethod.IsGenericMethod)
                        throw new VRpcException($"Generic methods are not supported. Method name: '{ifaceMethod.Name}'");

                    Type returnType = ifaceMethod.ReturnType;

                    ParameterInfo[] parameters = ifaceMethod.GetParameters();
                    Type[] returnTypes = parameters.Select(x => x.ParameterType).ToArray();
                    MethodBuilder ifaceMethodImpl = classType.DefineMethod(ifaceMethod.Name, ImplicitImplementation, returnType, returnTypes);

                    int methodId = methodCount++;
                    FieldBuilder fieldMethodInfo = classType.DefineField($"_method{methodId}", typeof(MethodInfo), FieldAttributes.Private | FieldAttributes.InitOnly);

                    fields.Add(new FieldMethodInfo(fieldMethodInfo.Name, ifaceMethod));
                    methodsDict.Add(methodId, ifaceMethod);

                    ILGenerator il = ifaceMethodImpl.GetILGenerator();

                    if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                    {
                        // Тип результата задачи.
                        Type taskReturnType = returnType.GenericTypeArguments[0];
                        var invokeMethod = _taskInvokeMethod.MakeGenericMethod(taskReturnType);
                        GenerateMethod(il, ifaceMethod, fieldMethodInfo, invokeMethod);
                    }
                    else if (returnType == typeof(Task))
                    {
                        GenerateMethod(il, ifaceMethod, fieldMethodInfo, _emptyTaskInvokeMethod);
                    }
                    else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
                    {
                        // Тип результата задачи.
                        Type taskReturnType = returnType.GenericTypeArguments[0];
                        var invokeMethod = _valueTaskInvokeMethod.MakeGenericMethod(taskReturnType);
                        GenerateMethod(il, ifaceMethod, fieldMethodInfo, invokeMethod);
                    }
                    else if (returnType == typeof(ValueTask))
                    {
                        GenerateMethod(il, ifaceMethod, fieldMethodInfo, _emptyValueTaskInvokeMethod);
                    }
                    else if (returnType == typeof(void))
                    // Метод синхронный.
                    {
                        GenerateMethod(il, ifaceMethod, fieldMethodInfo, _noresultInvokeMethod);
                    }
                    else
                    // Метод синхронный.
                    {
                        var invokeMethod = _invokeMethod.MakeGenericMethod(returnType);
                        GenerateMethod(il, ifaceMethod, fieldMethodInfo, invokeMethod);
                    }

                    //classType.DefineMethodOverride(methodBuilder, method);
                }
            }

            TypeInfo? ti = classType.CreateTypeInfo();
            Debug.Assert(ti != null);
            //Type dynamicType = classType.CreateType();

            TClass proxy;
            if (instance != null)
            {
                proxy = (TClass)Activator.CreateInstance(ti, args: instance);
            }
            else
            {
                proxy = (TClass)Activator.CreateInstance(ti);
            }

            foreach (var item in fields)
            {
                FieldInfo? field = ti.GetField(item.FieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                Debug.Assert(field != null);
                field.SetValue(proxy, item.MethodInfo);
            }

            return source == null ? proxy : CopyValues(source, proxy);
        }

        private static void GenerateMethod(ILGenerator il, MethodInfo method, FieldInfo methodInfoField, MethodInfo proxyMethod)
        {
            ParameterInfo[] parameters = method.GetParameters();

            // Объявить переменную object[] args.
            LocalBuilder localVariable = il.DeclareLocal(typeof(object[]));

            // Загрузить инстанс this.
            il.Emit(OpCodes.Ldarg_0);

            // Загрузить в стек ссылку на _methodInfo.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, methodInfoField);

            // Размер массива args.
            il.Emit_Ldc_I4(parameters.Length);

            // Объявляем object[] args = new object[x];
            il.Emit(OpCodes.Newarr, typeof(object));

            bool hasOutArgs = parameters.Any(x => x.ParameterType.IsByRef);

            // Что-бы скопировать ref и out переменные.
            var outVarList = new List<ParamMethodInfo>();

            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                Type? parameterType = parameter.ParameterType;

                if (hasOutArgs)
                {
                    if (parameterType.IsByRef)
                    // Параметр по ссылке.
                    {
                        // В конце нужно скопировать значения обратно в Out и Ref параметры.
                        outVarList.Add(new ParamMethodInfo(parameter, i));

                        // ref Type => Type (System.Int32& => System.Int32).
                        parameterType = parameterType.GetElementType();

                        il.Emit(OpCodes.Dup);
                        il.Emit_Ldc_I4(i);
                        il.Emit_Ldarg(i + 1);

                        if (parameterType.IsValueType)
                        {
                            // Записать в стек значение по ссылке.
                            il.Emit(OpCodes.Ldobj, parameterType);
                            il.Emit(OpCodes.Box, parameterType);
                        }
                        else
                        {
                            // Загрузить ЗНАЧЕНИЕ ref переменной в стек.
                            il.Emit(OpCodes.Ldind_Ref);
                        }

                        // Записать значение в массив args.
                        il.Emit(OpCodes.Stelem_Ref);
                    }
                    else
                    {
                        // Загрузить аргумент из массива в стек.
                        il.Emit(OpCodes.Dup);
                        il.Emit_Ldc_I4(i);
                        il.Emit_Ldarg(i + 1);

                        // Значимый тип следует упаковать.
                        if (parameterType.IsValueType)
                            il.Emit(OpCodes.Box, parameterType);

                        il.Emit(OpCodes.Stelem_Ref);
                    }
                }
                else
                {
                    // Загрузить аргумент из массива в стек.
                    il.Emit(OpCodes.Dup);
                    il.Emit_Ldc_I4(i);
                    il.Emit_Ldarg(i + 1);

                    // Значимый тип следует упаковать.
                    if (parameterType.IsValueType)
                        il.Emit(OpCodes.Box, parameterType);

                    il.Emit(OpCodes.Stelem_Ref);
                }
            }

            if (hasOutArgs)
            {
                // Объявить локальную переменную method.
                il.Emit(OpCodes.Stloc_0);
                
                // Загрузить второй аргумент функции "args" в стек из локальной переменной.
                il.Emit(OpCodes.Ldloc_0);

                // Вызвать метод.
                il.Emit(OpCodes.Callvirt, proxyMethod);

                #region Копирование Out и Ref параметров

                // Копируем значения локальных out и ref параметров обратно в массив object[] args.
                foreach (var v in outVarList)
                {
                    il.Emit_Ldarg(v.Index + 1);

                    // Загрузить в стек args.
                    il.Emit(OpCodes.Ldloc_0);

                    // Индекс массива args.
                    il.Emit_Ldc_I4(v.Index);

                    // refObj = array[1];
                    il.Emit(OpCodes.Ldelem_Ref);

                    // ref Type => Type (System.Int32& => System.Int32).
                    Type paramType = v.Param.ParameterType.GetElementType();
                    if (paramType.IsValueType)
                    {
                        // Распаковать значимый тип.
                        il.Emit(OpCodes.Unbox_Any, paramType);

                        // Преобразование типа.
                        il.Emit(OpCodes.Stobj, paramType);
                    }
                    else
                    {
                        // Записывает значение в ref.
                        il.Emit(OpCodes.Stind_Ref);
                    }
                }
                #endregion
            }
            else
            {
                // Вызвать метод.
                il.CallMethod(proxyMethod);
            }

            // Возврат результата.
            if (method.ReturnType == typeof(void))
            {
                // Удалить результат функции из стека.
                //il.Emit(OpCodes.Pop);
            }
            else
            {
                if (method.ReturnType.IsValueType)
                {
                    // Распаковать возвращённый результат что-бы вернуть как object.
                    //il.Emit(OpCodes.Unbox_Any, method.ReturnType);
                }
                else
                {
                    // Не нужно кастовать если возвращаемый тип интерфейса тоже object.
                    //if (method.ReturnType != typeof(object))
                    //{
                        //il.Emit(OpCodes.Isinst, method.ReturnType); // Каст с помощью as.
                    //}
                }
            }
            il.Emit(OpCodes.Ret);
        }

        private static K CopyValues<K>(TClass source, K destination) where K : notnull
        {
            foreach (PropertyInfo property in source.GetType().GetProperties(_visibilityFlags))
            {
                var prop = destination.GetType().GetProperty(property.Name, _visibilityFlags);
                if (prop != null && prop.CanWrite)
                    prop.SetValue(destination, property.GetValue(source), null);
            }

            return destination;
        }
    }
}