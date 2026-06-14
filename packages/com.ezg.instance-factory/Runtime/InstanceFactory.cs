using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Ezg.Package.InstanceFactory
{
    /// <summary>
    ///     Factory utility for creating object instances efficiently using compiled expression trees.
    /// </summary>
    public static class InstanceFactory
    {
        #region Private Methods

        /// <summary>
        ///     Compiles and caches a creator delegate for the given key containing type signature.
        /// </summary>
        /// <param name="key">The tuple key defining the type and its constructor arguments.</param>
        /// <returns>The compiled creator delegate.</returns>
        private static CreateDelegate CacheFunc(Tuple<Type, Type, Type, Type> key)
        {
            var types = new[] { key.Item1, key.Item2, key.Item3, key.Item4 };
            var method = typeof(InstanceFactory)
                .GetMethods()
                .Where(m => m.Name == "CreateInstance").Single(m => m.GetParameters().Count() == 4);
            var generic = method.MakeGenericMethod(key.Item2, key.Item3, key.Item4);

            var paramExpr = new List<ParameterExpression>();
            paramExpr.Add(Expression.Parameter(typeof(Type)));
            for (var i = 0; i < 3; i++)
                paramExpr.Add(Expression.Parameter(typeof(object)));

            var callParamExpr = new List<Expression>();
            callParamExpr.Add(paramExpr[0]);
            for (var i = 1; i < 4; i++)
                callParamExpr.Add(Expression.Convert(paramExpr[i], types[i]));

            var callExpr = Expression.Call(generic, callParamExpr);
            var lambdaExpr = Expression.Lambda<CreateDelegate>(callExpr, paramExpr);
            var func = lambdaExpr.Compile();
            _cachedFuncs.TryAdd(key, func);
            return func;
        }

        #endregion

        #region Fields

        private delegate object CreateDelegate(Type type, object arg1, object arg2, object arg3);

        private static readonly ConcurrentDictionary<Tuple<Type, Type, Type, Type>, CreateDelegate>
            _cachedFuncs = new();

        #endregion

        #region Public Methods

        /// <summary>
        ///     Creates an instance of the specified type with no arguments.
        /// </summary>
        /// <param name="type">The type of object to create.</param>
        /// <returns>The created instance.</returns>
        public static object CreateInstance(Type type)
        {
            return InstanceFactoryGeneric<TypeToIgnore, TypeToIgnore, TypeToIgnore>.CreateInstance(type, null, null,
                null);
        }

        /// <summary>
        ///     Creates an instance of the specified type with one argument.
        /// </summary>
        /// <typeparam name="TArg1">The type of the first argument.</typeparam>
        /// <param name="type">The type of object to create.</param>
        /// <param name="arg1">The first argument.</param>
        /// <returns>The created instance.</returns>
        public static object CreateInstance<TArg1>(Type type, TArg1 arg1)
        {
            return InstanceFactoryGeneric<TArg1, TypeToIgnore, TypeToIgnore>.CreateInstance(type, arg1, null, null);
        }

        /// <summary>
        ///     Creates an instance of the specified type with two arguments.
        /// </summary>
        /// <typeparam name="TArg1">The type of the first argument.</typeparam>
        /// <typeparam name="TArg2">The type of the second argument.</typeparam>
        /// <param name="type">The type of object to create.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <returns>The created instance.</returns>
        public static object CreateInstance<TArg1, TArg2>(Type type, TArg1 arg1, TArg2 arg2)
        {
            return InstanceFactoryGeneric<TArg1, TArg2, TypeToIgnore>.CreateInstance(type, arg1, arg2, null);
        }

        /// <summary>
        ///     Creates an instance of the specified type with three arguments.
        /// </summary>
        /// <typeparam name="TArg1">The type of the first argument.</typeparam>
        /// <typeparam name="TArg2">The type of the second argument.</typeparam>
        /// <typeparam name="TArg3">The type of the third argument.</typeparam>
        /// <param name="type">The type of object to create.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <returns>The created instance.</returns>
        public static object CreateInstance<TArg1, TArg2, TArg3>(Type type, TArg1 arg1, TArg2 arg2, TArg3 arg3)
        {
            return InstanceFactoryGeneric<TArg1, TArg2, TArg3>.CreateInstance(type, arg1, arg2, arg3);
        }

        /// <summary>
        ///     Creates an instance of the specified type with a variable list of arguments.
        /// </summary>
        /// <param name="type">The type of object to create.</param>
        /// <param name="args">The arguments array.</param>
        /// <returns>The created instance.</returns>
        public static object CreateInstance(Type type, params object[] args)
        {
            if (args == null)
                return CreateInstance(type);

            if (args.Length > 3 ||
                (args.Length > 0 && args[0] == null) ||
                (args.Length > 1 && args[1] == null) ||
                (args.Length > 2 && args[2] == null))
                return Activator.CreateInstance(type, args);

            var arg0 = args.Length > 0 ? args[0] : null;
            var arg1 = args.Length > 1 ? args[1] : null;
            var arg2 = args.Length > 2 ? args[2] : null;

            var key = Tuple.Create(
                type,
                arg0?.GetType() ?? typeof(TypeToIgnore),
                arg1?.GetType() ?? typeof(TypeToIgnore),
                arg2?.GetType() ?? typeof(TypeToIgnore));

            if (_cachedFuncs.TryGetValue(key, out var func))
                return func(type, arg0, arg1, arg2);
            return CacheFunc(key)(type, arg0, arg1, arg2);
        }

        #endregion
    }

    /// <summary>
    ///     Generic factory utility helper for compiling creation expressions.
    /// </summary>
    /// <typeparam name="TArg1">The type of the first argument.</typeparam>
    /// <typeparam name="TArg2">The type of the second argument.</typeparam>
    /// <typeparam name="TArg3">The type of the third argument.</typeparam>
    public static class InstanceFactoryGeneric<TArg1, TArg2, TArg3>
    {
        #region Fields

        private static readonly ConcurrentDictionary<Type, Func<TArg1, TArg2, TArg3, object>> cachedFuncs = new();

        #endregion

        #region Public Methods

        /// <summary>
        ///     Creates an instance of the specified type with three arguments.
        /// </summary>
        /// <param name="type">The type of object to create.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <returns>The created instance.</returns>
        public static object CreateInstance(Type type, TArg1 arg1, TArg2 arg2, TArg3 arg3)
        {
            if (cachedFuncs.TryGetValue(type, out var func))
                return func(arg1, arg2, arg3);
            return CacheFunc(type, arg1, arg2, arg3)(arg1, arg2, arg3);
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Compiles and caches a constructor expression function for a given type.
        /// </summary>
        /// <param name="type">The type of object to create.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <returns>The compiled creation function.</returns>
        private static Func<TArg1, TArg2, TArg3, object> CacheFunc(Type type, TArg1 arg1, TArg2 arg2, TArg3 arg3)
        {
            var constructorTypes = new List<Type>();
            if (typeof(TArg1) != typeof(TypeToIgnore))
                constructorTypes.Add(typeof(TArg1));
            if (typeof(TArg2) != typeof(TypeToIgnore))
                constructorTypes.Add(typeof(TArg2));
            if (typeof(TArg3) != typeof(TypeToIgnore))
                constructorTypes.Add(typeof(TArg3));

            var parameters = new List<ParameterExpression>
            {
                Expression.Parameter(typeof(TArg1)),
                Expression.Parameter(typeof(TArg2)),
                Expression.Parameter(typeof(TArg3))
            };

            var constructor = type.GetConstructor(constructorTypes.ToArray());
            var constructorParameters = parameters.Take(constructorTypes.Count).ToList();
            var newExpr = Expression.New(constructor, constructorParameters);
            var lambdaExpr = Expression.Lambda<Func<TArg1, TArg2, TArg3, object>>(newExpr, parameters);
            var func = lambdaExpr.Compile();
            cachedFuncs.TryAdd(type, func);
            return func;
        }

        #endregion
    }

    /// <summary>
    ///     Placeholder class representing an ignored parameter type.
    /// </summary>
    public class TypeToIgnore
    {
    }
}