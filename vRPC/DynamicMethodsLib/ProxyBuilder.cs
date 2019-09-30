using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace DynamicMethodsLib
{
    internal static class ProxyBuilder<TParent>
    {
        private const BindingFlags _visibilityFlags = BindingFlags.Public | BindingFlags.Instance;
        private static readonly MethodInfo _invokeMethod = typeof(TParent).GetMethod("Invoke",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(MethodInfo), typeof(object[]) },
                modifiers: null);

        static ProxyBuilder()
        {
            Debug.Assert(_invokeMethod != null, $"У типа {typeof(TParent).FullName} должен быть метод Invoke(MethodInfo m, object[] args)");
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

        public static TParent CreateProxy<TIface>(TParent source = default, object instance = null)
        {
            var ifaceType = typeof(TIface);
            if (!ifaceType.IsPublic)
                throw new InvalidOperationException($"Интерфейс {ifaceType.FullName} должен быть публичным.");

            var proxyParentClassType = typeof(TParent);
            if(proxyParentClassType.IsSealed)
                throw new InvalidOperationException($"Родительский класс {proxyParentClassType.FullName} не должен быть запечатанным.");

            string assemblyName = typeof(TIface).Name + "_" + Guid.NewGuid().ToString();
            AssemblyBuilder assemblyBuilder = DefineDynamicAssembly(assemblyName);
            ModuleBuilder moduleBuilder = DefineDynamicModule(assemblyBuilder);
            string className = proxyParentClassType.Name + "_" + typeof(TIface).Name;
            TypeBuilder classType = moduleBuilder.DefineType(className, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, parent: proxyParentClassType);
            var fieldsList = new List<string>();

            classType.AddInterfaceImplementation(typeof(TIface));

            if (instance != null)
            // В конструктор должен передаваться инстанс.
            {
                Type instanceType = instance.GetType();

                // Пустой конструктор базового типа.
                ConstructorInfo baseDefaultCtor = proxyParentClassType.GetConstructor(Type.EmptyTypes);

                // Базовый конструктор с параметром.
                ConstructorInfo baseCtor = null;
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
                    throw new InvalidOperationException($"Не найден конструктор принимающий один параметр типа {typeof(TIface).FullName}.");

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
                ConstructorInfo baseDefaultCtor = proxyParentClassType.GetConstructor(Type.EmptyTypes);
                if (baseDefaultCtor == null)
                    throw new InvalidOperationException($"У типа {proxyParentClassType.FullName} должен быть пустой и открытый конструктор.");
            }

            int methodCount = 0;
            var methodsDict = new Dictionary<int, MethodInfo>();
            MethodInfo[] methods = typeof(TIface).GetMethods();
            var fields = new List<FieldMethodInfo>(methods.Length);

            foreach (PropertyInfo v in typeof(TIface).GetProperties())
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

                classType.DefineMethodOverride(getter, v.GetGetMethod());
                classType.DefineMethodOverride(setter, v.GetSetMethod());
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

            foreach (MethodInfo method in typeof(TIface).GetMethods())
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

                if (!method.IsSpecialName)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    Type[] returnTypes = parameters.Select(x => x.ParameterType).ToArray();
                    MethodBuilder methodBuilder = classType.DefineMethod(method.Name, ImplicitImplementation, method.ReturnType, returnTypes);

                    int methodId = methodCount++;
                    FieldBuilder fieldMethodInfo = classType.DefineField($"_method{methodId}", typeof(MethodInfo), FieldAttributes.Private | FieldAttributes.InitOnly);

                    fields.Add(new FieldMethodInfo(fieldMethodInfo.Name, method));
                    methodsDict.Add(methodId, method);

                    ILGenerator il = methodBuilder.GetILGenerator();
                    GenerateMethod(il, method, fieldMethodInfo);

                    //classType.DefineMethodOverride(methodBuilder, method);
                }
            }

            TypeInfo ti = classType.CreateTypeInfo();
            //Type dynamicType = classType.CreateType();

            TParent proxy;
            if (instance != null)
            {
                proxy = (TParent)Activator.CreateInstance(ti, args: instance);
            }
            else
            {
                proxy = (TParent)Activator.CreateInstance(ti);
            }

            foreach (var item in fields)
            {
                FieldInfo field = ti.GetField(item.FieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                field.SetValue(proxy, item.MethodInfo);
            }

            return source == null ? proxy : CopyValues(source, proxy);
        }

        private static void GenerateMethod(ILGenerator il, MethodInfo method, FieldInfo methodInfoField)
        {
            ParameterInfo[] parameters = method.GetParameters();

            // Объявить переменную args.
            LocalBuilder localVariable = il.DeclareLocal(typeof(object[]));

            // Загрузить инстанс this.
            il.Emit(OpCodes.Ldarg_0);

            // Загрузить в стек ссылку на _methodInfo.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, methodInfoField);

            // Размер массива args.
            il.Emit_Ldc_I4(parameters.Length);

            // Объявляем object[] args.
            il.Emit(OpCodes.Newarr, typeof(object));

            bool hasOutArgs = parameters.Any(x => x.ParameterType.IsByRef);

            // Что-бы скопировать ref и out переменные.
            var outVarList = new List<ParamMethodInfo>();

            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                Type parameterType = parameter.ParameterType;

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
                il.Emit(OpCodes.Callvirt, _invokeMethod);

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
                il.CallMethod(_invokeMethod);
            }

            // Возврат результата.
            if (method.ReturnType == typeof(void))
            {
                // Удалить результат функции из стека.
                il.Emit(OpCodes.Pop);
            }
            else
            {
                if (method.ReturnType.IsValueType)
                {
                    // Распаковать возвращённый результат что-бы вернуть как object.
                    il.Emit(OpCodes.Unbox_Any, method.ReturnType);
                }
                else
                {
                    // Не нужно кастовать если возвращаемый тип совпадает.
                    if (method.ReturnType != typeof(object))
                        il.Emit(OpCodes.Castclass, method.ReturnType);
                }
            }
            il.Emit(OpCodes.Ret);
        }

        private static K CopyValues<K>(TParent source, K destination)
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