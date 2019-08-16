using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace DynamicMethodsLib
{
    public static class DynamicExpressionFactory
    {
        public static Func<object, object[], object> CreateMethodDelegate(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();

            var instanceParameter = Expression.Parameter(typeof(object), "instance");
            var argsParameter = Expression.Parameter(typeof(object[]), "args");

            // Creating an expression to hold a local variable.   
            var result = Expression.Parameter(typeof(object), "callResult");

            var expressions = new List<Expression>();
            var expressionsOutArgs = new List<Expression>();
            var callArguments = new Expression[parameters.Length];
            var variables = new List<ParameterExpression> { result };

            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo para = parameters[i];
                var arrayAccess = Expression.ArrayAccess(argsParameter, Expression.Constant(i, typeof(int)));

                if (para.ParameterType.IsByRef)
                // ref и out параметры.
                {
                    Type parameterType = para.ParameterType.GetElementType();
                    var outRefParam = Expression.Parameter(parameterType);
                    callArguments[i] = outRefParam;
                    variables.Add(outRefParam);

                    // args[i] = (Object)outRefParam;
                    expressionsOutArgs.Add(Expression.Assign(arrayAccess, Expression.Convert(outRefParam, typeof(object))));

                    if (para.IsOut)
                    {
                        expressions.Add(Expression.Assign(outRefParam, Expression.Default(parameterType)));
                    }
                    else
                    {
                        // Скопировать параметр массива в ref переменную.
                        expressions.Add(Expression.Assign(outRefParam, Expression.Convert(arrayAccess, parameterType)));
                    }
                }
                else
                {
                    if (para.ParameterType.IsValueType)
                    {
                        if (para.ParameterType.IsPrimitive)
                        {
                            // for primitive types we need to handle type widening (e.g. short -> int)
                            MethodInfo toParameterTypeMethod = typeof(IConvertible)
                                .GetMethod("To" + para.ParameterType.Name, new[] { typeof(IFormatProvider) });

                            if (toParameterTypeMethod != null)
                            {
                                var targetValue = Expression.Parameter(para.ParameterType);
                                var convertible = Expression.Parameter(typeof(IConvertible));

                                variables.Add(targetValue);
                                variables.Add(convertible);

                                callArguments[i] = targetValue;

                                expressions.Add(
                                    // if(args[i] is Type) {
                                    Expression.IfThenElse(Expression.TypeIs(arrayAccess, para.ParameterType),
                                        // true: targetValue = (Type)args[i]
                                        Expression.Assign(targetValue, Expression.Convert(arrayAccess, para.ParameterType)),
                                        // else:
                                        Expression.IfThenElse(
                                            // if((convertible = value as IConvertible) != null)
                                            Expression.NotEqual(Expression.Assign(convertible, Expression.TypeAs(arrayAccess, typeof(IConvertible))), Expression.Constant(null, typeof(IConvertible))),
                                                // true: targetValue = convertible.ToType();
                                                Expression.Assign(targetValue, Expression.Call(convertible, toParameterTypeMethod, Expression.Constant(null, typeof(IFormatProvider)))),
                                                // false: targetValue = (Type)args[i];
                                                Expression.Assign(targetValue, Expression.Convert(arrayAccess, para.ParameterType))
                                    ))
                                );
                                continue;
                            }
                        }
                    }
                    // Напрямую кастуем объект в ожидаемый тип.
                    callArguments[i] = Expression.Convert(arrayAccess, para.ParameterType);
                }
            }

            var methodCall = Expression.Call(
                        instance: Expression.Convert(instanceParameter, method.DeclaringType),
                        method: method,
                        arguments: callArguments);

            if (method.ReturnType != typeof(void))
            {
                expressions.Add(Expression.Assign(result, Expression.Convert(methodCall, typeof(object))));
            }
            else
            {
                expressions.Add(methodCall);
            }

            // Скопировать out/ref параметры обратно в массив args.
            expressions.AddRange(expressionsOutArgs);

            // Последнее выражение является результатом лямбды.
            expressions.Add(result);

            var lambda2 = Expression.Lambda<Func<object, object[], object>>(Expression.Block(variables: variables, expressions: expressions), instanceParameter, argsParameter);
            Func<object, object[], object> invokeDelagate = lambda2.Compile();
            return invokeDelagate;
        }
    }
}
