﻿using System;
using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using Pchp.Core.Reflection;

namespace Pchp.Core.Dynamic
{
    /// <summary>
    /// Helper methods for converting expressions.
    /// </summary>
    internal static class ConvertExpression
    {
        #region Bind

        /// <summary>
        /// Creates expression that converts <paramref name="arg"/> to <paramref name="target"/> type.
        /// </summary>
        /// <param name="arg">Source expression to be converted.</param>
        /// <param name="target">Target type.</param>
        /// <param name="ctx">Expression with current context.</param>
        /// <returns>Expression converting <paramref name="arg"/> to <paramref name="target"/> type.</returns>
        public static Expression Bind(Expression arg, Type target, Expression ctx)
        {
            if (arg.Type == target)
            {
                return arg;
            }

            // dereference
            if (arg.Type == typeof(PhpAlias))
            {
                return Bind(Expression.Field(arg, Cache.PhpAlias.Value), target, ctx);
            }

            Debug.Assert(ctx != null, "!ctx");

            //
            if (target == typeof(long)) return BindToLong(arg);
            if (target == typeof(int) ||
                target == typeof(uint) ||
                target == typeof(ulong)) return Expression.Convert(BindToLong(arg), target);
            if (target == typeof(double)) return BindToDouble(arg);
            if (target == typeof(float)) return Expression.Convert(BindToDouble(arg), target);  // (float)double
            if (target == typeof(string)) return BindToString(arg, ctx);
            if (target == typeof(bool)) return BindToBool(arg);
            if (target == typeof(PhpNumber)) return BindToNumber(arg);
            if (target == typeof(PhpValue)) return BindToValue(arg);
            if (target == typeof(void)) return BindToVoid(arg);
            if (target == typeof(object)) return BindAsObject(arg);
            //if (target == typeof(stdClass)) return BindAsStdClass(arg);
            if (target == typeof(PhpArray) ||
                target == typeof(IPhpArray) ||
                target == typeof(IPhpEnumerable) ||
                target == typeof(PhpHashtable)) return BindToArray(arg);   // TODO: BindToXXXX(), cast object to IPhpEnumerable if Value.IsObject
            if (target == typeof(IntStringKey)) return BindIntStringKey(arg);
            if (target == typeof(IPhpCallable)) return BindAsCallable(arg);
            if (target == typeof(PhpString)) return BindToPhpString(arg, ctx);
            if (target == typeof(byte[])) return Expression.Call(BindToPhpString(arg, ctx), Cache.PhpString.ToBytes_Context, ctx);
            if (target == typeof(PhpAlias))
            {
                if (arg.Type == typeof(PhpValue)) return Expression.Call(arg, Cache.Operators.PhpValue_EnsureAlias);
                return Expression.New(Cache.PhpAlias.ctor_PhpValue_int, BindToValue(arg), Expression.Constant(1));
            }
            
            // enum
            if (target.IsEnum)
            {
                return Expression.Convert(BindToLong(arg), target);
            }

            // 
            if (target.IsValueType == false)
            {
                return BindAsReferenceType(arg, target);
            }

            if (target == typeof(IntPtr))
            {
                return Expression.New(typeof(IntPtr).GetCtor(Cache.Types.Long), BindToLong(arg));
            }

            //
            throw new NotImplementedException(target.ToString());
        }

        private static Expression BindToLong(Expression expr)
        {
            var source = expr.Type;

            if (source == typeof(int) || source == typeof(uint) ||
                source == typeof(double) || source == typeof(float))
            {
                // (long)expr
                return Expression.Convert(expr, typeof(long));
            }

            if (source == typeof(PhpNumber)) return Expression.Call(expr, typeof(PhpNumber).GetMethod("ToLong", Cache.Types.Empty));
            if (source == typeof(PhpArray)) return Expression.Call(expr, typeof(PhpArray).GetMethod("ToLong", Cache.Types.Empty));
            if (source == typeof(string)) return Expression.Call(Cache.Operators.ToLong_String, expr);
            if (source == typeof(PhpString)) return Expression.Call(expr, typeof(PhpString).GetMethod("ToLong", Cache.Types.Empty));
            if (source == typeof(void)) return VoidAsConstant(expr, 0L, typeof(long));
            if (source == typeof(long)) return expr;    // unreachable

            // TODO: following conversions may fail, we should report it failed and throw an error
            if (source == typeof(PhpValue)) return Expression.Call(expr, typeof(PhpValue).GetMethod("ToLong", Cache.Types.Empty));

            throw new NotImplementedException(source.FullName);
        }

        private static Expression BindToDouble(Expression expr)
        {
            var source = expr.Type;

            if (source == typeof(int) ||
                source == typeof(uint) ||
                source == typeof(long)) return Expression.Convert(expr, typeof(double));
            if (source == typeof(PhpNumber)) return Expression.Call(expr, typeof(PhpNumber).GetMethod("ToDouble", Cache.Types.Empty));
            if (source == typeof(PhpArray)) return Expression.Call(expr, typeof(PhpArray).GetMethod("ToDouble", Cache.Types.Empty));
            if (source == typeof(string)) return Expression.Call(Cache.Operators.ToDouble_String, expr);
            if (source == typeof(PhpString)) return Expression.Call(expr, typeof(PhpString).GetMethod("ToDouble", Cache.Types.Empty));
            if (source == typeof(void)) return VoidAsConstant(expr, 0.0, typeof(double));
            if (source == typeof(double)) return expr;
            if (source == typeof(float)) return Expression.Convert(expr, typeof(double));

            // TODO: following conversions may fail, we should report it failed and throw an error
            if (source == typeof(PhpValue)) return Expression.Call(expr, typeof(PhpValue).GetMethod("ToDouble", Cache.Types.Empty));

            throw new NotImplementedException(source.FullName);
        }

        public static Expression BindToBool(Expression expr)
        {
            var source = expr.Type;

            if (source == typeof(int)) return Expression.NotEqual(expr, Expression.Constant(0, Cache.Types.Int[0]));    // <int> != 0
            if (source == typeof(uint)) return Expression.NotEqual(expr, Expression.Constant((uint)0, typeof(uint)));    // <uint> != 0
            if (source == typeof(long)) return Expression.NotEqual(expr, Expression.Constant(0L, Cache.Types.Long[0]));    // <long> != 0
            if (source == typeof(PhpNumber)) return Expression.Call(expr, typeof(PhpNumber).GetMethod("ToBoolean", Cache.Types.Empty));
            if (source == typeof(PhpArray)) return Expression.Call(expr, typeof(PhpArray).GetMethod("ToBoolean", Cache.Types.Empty));
            if (source == typeof(PhpValue)) return Expression.Call(expr, typeof(PhpValue).GetMethod("ToBoolean", Cache.Types.Empty));
            if (source == typeof(void)) return VoidAsConstant(expr, false, typeof(bool));
            if (source == typeof(bool)) return expr;

            return Expression.Call(Cache.Operators.ToBoolean_Object, expr);
        }

        private static Expression BindToString(Expression expr, Expression ctx)
        {
            var source = expr.Type;

            if (source == typeof(int) ||
                source == typeof(uint) ||
                source == typeof(long))
                return Expression.Call(expr, Cache.Object.ToString);

            if (source == typeof(double))
                return Expression.Call(Cache.Object.ToString_Double_Context, expr, ctx);

            if (source == typeof(float))
                return Expression.Call(Cache.Object.ToString_Double_Context, Expression.Convert(expr, typeof(double)), ctx);    // ToString((double)expr, ctx)

            if (source == typeof(bool))
                return Expression.Call(Cache.Object.ToString_Bool, expr);

            if (source == typeof(string))
                return expr;

            if (source == typeof(PhpString))
                return Expression.Call(expr, Cache.PhpString.ToString_Context, ctx); ;

            if (source == typeof(PhpValue))
                return Expression.Call(expr, Cache.Operators.PhpValue_ToString_Context, ctx);

            if (source == typeof(void))
                return VoidAsConstant(expr, string.Empty, typeof(string));

            if (source == typeof(PhpNumber))
                return Expression.Call(expr, Cache.Operators.PhpNumber_ToString_Context, ctx);

            if (source == typeof(object))
            {
                // NULL
                if (expr is ConstantExpression && ((ConstantExpression)expr).Value == null)
                {
                    // (string)null
                    return Expression.Constant(string.Empty, typeof(string));
                }

                // __toString

            }

            throw new NotImplementedException(source.FullName);
        }

        private static Expression BindToPhpString(Expression expr, Expression ctx)
        {
            var source = expr.Type;

            //
            if (source == typeof(PhpString))
            {
                return expr;
            }

            // string -> PhpString
            if (source == typeof(int) ||
                source == typeof(uint) ||
                source == typeof(long) ||
                source == typeof(float) || source == typeof(double))   // TODO: ToString_Double_Context
            {
                expr = Expression.Call(expr, Cache.Object.ToString);
                source = expr.Type;
            }

            if (source == typeof(PhpValue))
            {
                return Expression.Call(Cache.Operators.ToPhpString_PhpValue_Context, expr, ctx);    // Convert.ToPhpString(PhpValue, Context)
            }

            if (source == typeof(string)) return Expression.New(Cache.PhpString.ctor_String, expr);        // new PhpString(string)
            if (source == typeof(byte[])) return Expression.New(Cache.PhpString.ctor_ByteArray, expr);     // new PhpString(byte[])

            //
            throw new NotImplementedException(source.FullName);
        }

        private static Expression BindIntStringKey(Expression expr)
        {
            var source = expr.Type;

            if (source == typeof(int)) return Expression.New(Cache.IntStringKey.ctor_Int, expr);
            if (source == typeof(long)) return Expression.New(Cache.IntStringKey.ctor_Int, Expression.Convert(expr, Cache.Types.Int[0]));
            if (source == typeof(string)) return Expression.New(Cache.IntStringKey.ctor_String, expr);

            // following conversions may throw an exception
            if (source == typeof(PhpValue)) return Expression.Call(expr, Cache.Operators.PhpValue_ToIntStringKey);

            throw new NotImplementedException(source.FullName);
        }

        private static Expression BindToNumber(Expression expr)
        {
            var source = expr.Type;

            //
            if (source == typeof(int) || source == typeof(uint))
            {
                source = typeof(long);
                expr = Expression.Convert(expr, typeof(long));
            }

            //
            if (source == typeof(long)) return Expression.Call(typeof(PhpNumber).GetMethod("Create", Cache.Types.Long), expr);
            if (source == typeof(double)) return Expression.Call(typeof(PhpNumber).GetMethod("Create", Cache.Types.Double), expr);
            if (source == typeof(float)) return Expression.Call(typeof(PhpNumber).GetMethod("Create", Cache.Types.Double), Expression.Convert(expr, typeof(double)));
            if (source == typeof(void)) return VoidAsConstant(expr, PhpNumber.Default, typeof(PhpNumber));
            if (source == typeof(PhpNumber)) return expr;
            if (source == typeof(PhpValue)) return Expression.Convert(expr, typeof(PhpNumber));
            if (source == typeof(string)) return Expression.Call(Cache.Operators.ToPhpNumber_String, expr);

            throw new NotImplementedException(source.FullName);
        }

        public static Expression BindToValue(Expression expr)
        {
            var source = expr.Type;

            //
            if (source == typeof(PhpValue)) return expr;
            if (source == typeof(bool)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.Bool), expr);
            if (source == typeof(int)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.Int), expr);
            if (source == typeof(long)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.Long), expr);
            if (source == typeof(uint)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.Long), Expression.Convert(expr, typeof(long)));
            if (source == typeof(double)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.Double), expr);
            if (source == typeof(float)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.Double), Expression.Convert(expr, typeof(double)));
            if (source == typeof(string)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.String), expr);
            if (source == typeof(PhpString)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.PhpString), expr);
            if (source == typeof(PhpNumber)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.PhpNumber), expr);
            if (source == typeof(PhpArray)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.PhpArray), expr);
            if (source == typeof(PhpAlias)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.PhpAlias), expr);   // PhpValue.Create(PhpAlias)

            if (source.IsValueType)
            {
                if (source == typeof(void)) return VoidAsConstant(expr, PhpValue.Void, Cache.Types.PhpValue[0]);

                throw new NotImplementedException(source.FullName);
            }
            else if (
                source == typeof(IPhpArray) ||
                source == typeof(object) ||
                typeof(ICollection).IsAssignableFrom(source) || // possibly PhpArray
                source == typeof(IPhpConvertible) ||
                source == typeof(IPhpEnumerable))
            {
                // convert dynamically to a PhpValue
                return Expression.Call(typeof(PhpValue).GetMethod("FromClr", Cache.Types.Object), expr);
            }
            else
            {
                // source is a class:
                return Expression.Call(typeof(PhpValue).GetMethod("FromClass", Cache.Types.Object), expr);
            }
        }

        private static Expression BindToClass(Expression expr)
        {
            var source = expr.Type;

            if (source == typeof(PhpValue)) return Expression.Call(expr, Cache.Operators.PhpValue_ToClass);
            if (source == typeof(PhpArray)) return Expression.Call(expr, Cache.Operators.PhpArray_ToClass);
            if (source == typeof(PhpNumber)) return Expression.Call(expr, typeof(PhpNumber).GetMethod("ToClass", Cache.Types.Empty));

            if (!source.IsValueType) return expr;

            throw new NotImplementedException(source.FullName);
        }

        static Expression BindAsObject(Expression expr)
        {
            var source = expr.Type;

            // PhpValue.AsObject
            if (source == typeof(PhpValue))
            {
                return Expression.Call(expr, Cache.Operators.PhpValue_AsObject);
            }

            // <expr>
            if (!source.IsValueType &&
                !source.IsSubclassOf(typeof(PhpResource)) &&
                !source.IsSubclassOf(typeof(IPhpArray)) &&
                !source.IsSubclassOf(typeof(PhpString))
                )
            {
                return expr;
            }

            // NULL
            return Expression.Constant(null, Cache.Types.Object[0]);
        }

        static Expression BindAsReferenceType(Expression expr, Type target)
        {
            Debug.Assert(expr.Type != typeof(PhpAlias));

            // from PhpValue:
            if (expr.Type == typeof(PhpValue))
            {
                expr = Expression.Call(expr, Cache.Operators.PhpValue_GetValue);    // dereference
                expr = Expression.Property(expr, Cache.Properties.PhpValue_Object); // PhpValue.Object
            }

            // just cast:
            return Expression.Convert(expr, target);
        }

        private static Expression BindToArray(Expression expr)
        {
            var source = expr.Type;

            if (source == typeof(PhpArray)) return expr;
            if (source == typeof(PhpValue)) return Expression.Call(expr, Cache.Operators.PhpValue_ToArray);
            if (source == typeof(object) && expr is ConstantExpression c && c.Value == null) return Expression.Constant(null, typeof(PhpArray));

            throw new NotImplementedException(source.FullName);
        }

        private static Expression BindAsCallable(Expression expr)
        {
            var source = expr.Type;

            if (typeof(IPhpCallable).IsAssignableFrom(source)) return expr;

            return Expression.Call(BindToValue(expr), Cache.Operators.PhpValue_AsCallable_RuntimeTypeHandle, Expression.Default(typeof(RuntimeTypeHandle)));    // TODO: call context instead of default()
        }

        private static Expression BindToVoid(Expression expr)
        {
            var source = expr.Type;

            if (source != typeof(void))
            {
                return Expression.Block(typeof(void), expr);
            }
            else
            {
                return expr;
            }
        }

        internal static Expression VoidAsConstant(Expression expr, object value, Type type)
        {
            Debug.Assert(expr.Type == typeof(void));

            // block{ expr; return constant; }

            var constant = Expression.Constant(value, type);

            return Expression.Block(expr, constant);
        }

        #endregion

        #region BindDefault

        public static Expression BindDefault(Type t)
        {
            if (t == typeof(PhpValue)) return Expression.Field(null, Cache.Properties.PhpValue_Void);
            if (t == typeof(PhpNumber)) return Expression.Field(null, Cache.Properties.PhpNumber_Default);

            return Expression.Default(t);
        }

        #endregion

        #region BindCost

        /// <summary>
        /// Creates expression that calculates cost of conversion from <paramref name="arg"/> to type <paramref name="target"/>.
        /// In some cases, returned expression is a constant and can be used in compile time.
        /// </summary>
        /// <param name="arg">Expression to be converted.</param>
        /// <param name="target">Target type.</param>
        /// <returns>Expression calculating the cost of conversion.</returns>
        public static Expression BindCost(Expression arg, Type target)
        {
            Debug.Assert(arg != null && target != null);

            var t = arg.Type;
            if (t == target)
            {
                return Expression.Constant(ConversionCost.Pass);
            }

            if (t == typeof(PhpAlias))
            {
                return BindCost(Expression.Field(arg, Cache.PhpAlias.Value), target);
            }

            if (target == typeof(PhpAlias) || target == typeof(PhpValue))
            {
                return Expression.Constant(ConversionCost.PassCostly);
            }

            if (t == typeof(PhpValue)) return BindCostFromValue(arg, target);
            if (t == typeof(double) || t == typeof(float)) return Expression.Constant(BindCostFromDouble(arg, target));
            if (t == typeof(long) || t == typeof(int) || t == typeof(uint)) return Expression.Constant(BindCostFromLong(arg, target));
            if (t == typeof(PhpNumber)) return BindCostFromNumber(arg, target);
            if (t == typeof(string)) return Expression.Constant(BindCostFromString(arg, target));
            if (t == typeof(PhpString)) return Expression.Constant(BindCostFromPhpString(arg, target));
            if (t == typeof(PhpArray)) return BindCostFromPhpArray(arg, target);

            // other types
            if (target.IsAssignableFrom(t)) return Expression.Constant(ConversionCost.Pass);

            //
            throw new NotImplementedException($"costof({t} -> {target})");
        }

        static Expression BindCostFromValue(Expression arg, Type target)
        {
            // constant cases
            if (target == typeof(PhpValue) ||
                target == typeof(PhpAlias)) return Expression.Constant(ConversionCost.Pass);

            if (!target.IsValueType)
            {
                // TODO
            }

            //
            if (target.IsEnum)
            {
                return Expression.Call(typeof(CostOf).GetMethod("ToInt64", arg.Type), arg);
            }

            if (target == typeof(PhpArray) || target == typeof(IPhpArray) || target == typeof(IPhpEnumerable) || target == typeof(PhpHashtable))
            {
                return Expression.Call(typeof(CostOf).GetMethod("ToPhpArray", arg.Type), arg);
            }

            //
            if (ReflectionUtils.IsPhpClassType(target))
            {
                var toclass_T = typeof(CostOf).GetTypeInfo().GetDeclaredMethod("ToClass").MakeGenericMethod(target);
                return Expression.Call(toclass_T, arg); // CostOf.ToClass<T>(arg)
            }

            // fallback
            return Expression.Call(typeof(CostOf).GetMethod("To" + target.Name, arg.Type), arg);
        }

        static ConversionCost BindCostFromDouble(Expression arg, Type target)
        {
            if (target == typeof(double) || target == typeof(float)) return (ConversionCost.Pass);
            if (target == typeof(PhpNumber)) return (ConversionCost.PassCostly);
            if (target == typeof(long) || target == typeof(int) || target == typeof(uint)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(bool)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(PhpValue)) return (ConversionCost.PassCostly);
            if (target == typeof(string) || target == typeof(PhpString)) return (ConversionCost.ImplicitCast);
            if (target == typeof(PhpArray)) return (ConversionCost.Warning);

            throw new NotImplementedException($"costof(double -> {target})");
        }

        static ConversionCost BindCostFromLong(Expression arg, Type target)
        {
            if (target == typeof(int) || target == typeof(long) || target == typeof(uint)) return (ConversionCost.Pass);
            if (target == typeof(PhpNumber)) return (ConversionCost.PassCostly);
            if (target == typeof(double) || target == typeof(float)) return (ConversionCost.ImplicitCast);
            if (target == typeof(bool)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(PhpValue)) return (ConversionCost.PassCostly);
            if (target == typeof(string) || target == typeof(PhpString)) return (ConversionCost.ImplicitCast);
            if (target == typeof(PhpArray)) return (ConversionCost.Warning);
            if (target == typeof(object)) return ConversionCost.PassCostly;    // TODO: Error when passing to a PHP function

            throw new NotImplementedException($"costof(long -> {target})");
        }

        static Expression BindCostFromNumber(Expression arg, Type target)
        {
            if (target == typeof(double) || target == typeof(long) || target == typeof(int) || target == typeof(uint) || target == typeof(float))
            {
                return Expression.Call(typeof(CostOf).GetMethod("To" + target.Name, arg.Type), arg);
            }

            if (target == typeof(PhpNumber)) return Expression.Constant(ConversionCost.Pass);
            if (target == typeof(string)) return Expression.Constant(ConversionCost.ImplicitCast);
            if (target == typeof(bool)) return Expression.Constant(ConversionCost.LoosingPrecision);
            if (target == typeof(PhpValue)) return Expression.Constant(ConversionCost.PassCostly);

            return Expression.Constant(ConversionCost.Warning);
        }

        static ConversionCost BindCostFromString(Expression arg, Type target)
        {
            if (target == typeof(bool)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(long) || target == typeof(uint) || target == typeof(int)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(double) || target == typeof(float)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(PhpNumber)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(string)) return (ConversionCost.Pass);
            if (target == typeof(PhpString)) return (ConversionCost.PassCostly);
            if (target == typeof(PhpValue)) return (ConversionCost.PassCostly);
            if (target == typeof(object)) return ConversionCost.PassCostly;    // TODO: Error when passing to a PHP function

            if (target.IsAssignableFrom(typeof(IPhpCallable))) throw new NotImplementedException("IPhpCallable");

            return ConversionCost.Error;
        }

        static ConversionCost BindCostFromPhpString(Expression arg, Type target)
        {
            if (target == typeof(bool)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(long) || target == typeof(uint) || target == typeof(int)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(double) || target == typeof(float)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(PhpNumber)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(string)) return (ConversionCost.PassCostly);
            if (target == typeof(PhpString)) return (ConversionCost.Pass);
            if (target == typeof(PhpValue)) return (ConversionCost.PassCostly);
            if (target == typeof(object)) return ConversionCost.PassCostly;    // TODO: Error when passing to a PHP function

            if (target.IsAssignableFrom(typeof(IPhpCallable))) throw new NotImplementedException("IPhpCallable");

            return ConversionCost.Error;
        }

        static Expression BindCostFromPhpArray(Expression arg, Type target)
        {
            if (target == typeof(string) || target == typeof(PhpString)) return Expression.Constant(ConversionCost.Warning);

            throw new NotImplementedException($"costof(array -> {target})");
        }

        #endregion
    }

    /// <summary>
    /// Runtime routines that calculates cost of conversion.
    /// </summary>
    public static class CostOf
    {
        /// <summary>
        /// Gets minimal value of given operands.
        /// </summary>
        public static ConversionCost Min(ConversionCost a, ConversionCost b) => (a < b) ? a : b;

        /// <summary>
        /// Gets maximal value of given operands.
        /// </summary>
        public static ConversionCost Max(ConversionCost a, ConversionCost b) => (a > b) ? a : b;

        public static ConversionCost Or(ConversionCost a, ConversionCost b) => a | b;

        #region CostOf

        public static ConversionCost ToInt32(PhpNumber value) => ToInt64(value);

        public static ConversionCost ToInt64(PhpNumber value) => value.IsLong ? ConversionCost.Pass : ConversionCost.LoosingPrecision;

        public static ConversionCost ToDouble(PhpNumber value) => value.IsLong ? ConversionCost.ImplicitCast : ConversionCost.Pass;

        public static ConversionCost ToBoolean(PhpValue value)
        {
            switch (value.TypeCode)
            {
                case PhpTypeCode.Boolean:
                    return ConversionCost.Pass;

                case PhpTypeCode.Int32:
                case PhpTypeCode.Long:
                case PhpTypeCode.Double:
                case PhpTypeCode.MutableString:
                case PhpTypeCode.String:
                    return ConversionCost.LoosingPrecision;

                case PhpTypeCode.PhpArray:
                    return ConversionCost.Warning;

                default:
                    return ConversionCost.NoConversion;
            }
        }

        public static ConversionCost ToInt32(PhpValue value) => ToInt64(value);

        public static ConversionCost ToInt64(PhpValue value)
        {
            switch (value.TypeCode)
            {
                case PhpTypeCode.Int32:
                case PhpTypeCode.Long:
                    return ConversionCost.Pass;

                case PhpTypeCode.Boolean:
                    return ConversionCost.ImplicitCast;

                case PhpTypeCode.Double:
                case PhpTypeCode.MutableString:
                case PhpTypeCode.String:
                    return ConversionCost.LoosingPrecision;

                case PhpTypeCode.PhpArray:
                    return ConversionCost.Warning;

                default:
                    return ConversionCost.NoConversion;
            }
        }

        public static ConversionCost ToString(PhpValue value)
        {
            switch (value.TypeCode)
            {
                case PhpTypeCode.Int32:
                case PhpTypeCode.Long:
                case PhpTypeCode.Boolean:
                case PhpTypeCode.Double:
                case PhpTypeCode.Object:
                    return ConversionCost.ImplicitCast;

                case PhpTypeCode.MutableString:
                    return value.MutableString.ContainsBinaryData ? ConversionCost.LoosingPrecision : ConversionCost.PassCostly;

                case PhpTypeCode.String:
                    return ConversionCost.Pass;

                case PhpTypeCode.PhpArray:
                    return ConversionCost.Warning;

                default:
                    return ConversionCost.NoConversion;
            }
        }

        public static ConversionCost ToPhpString(PhpValue value)
        {
            switch (value.TypeCode)
            {
                case PhpTypeCode.Int32:
                case PhpTypeCode.Long:
                case PhpTypeCode.Boolean:
                case PhpTypeCode.Double:
                case PhpTypeCode.Object:
                    return ConversionCost.ImplicitCast;

                case PhpTypeCode.MutableString:
                    return ConversionCost.Pass;

                case PhpTypeCode.String:
                    return ConversionCost.PassCostly;

                case PhpTypeCode.PhpArray:
                    return ConversionCost.Warning;

                default:
                    return ConversionCost.NoConversion;
            }
        }

        public static ConversionCost ToDouble(PhpValue value)
        {
            switch (value.TypeCode)
            {
                case PhpTypeCode.Int32:
                case PhpTypeCode.Long:
                case PhpTypeCode.Boolean:
                    return ConversionCost.ImplicitCast;

                case PhpTypeCode.Double:
                    return ConversionCost.Pass;

                case PhpTypeCode.MutableString:
                case PhpTypeCode.String:
                    return ConversionCost.LoosingPrecision;

                case PhpTypeCode.PhpArray:
                    return ConversionCost.Warning;

                default:
                    return ConversionCost.NoConversion;
            }
        }

        public static ConversionCost ToPhpNumber(PhpValue value)
        {
            switch (value.TypeCode)
            {
                case PhpTypeCode.Int32:
                case PhpTypeCode.Long:
                case PhpTypeCode.Double:
                    return ConversionCost.Pass;

                case PhpTypeCode.Boolean:
                    return ConversionCost.ImplicitCast;

                case PhpTypeCode.MutableString:
                case PhpTypeCode.String:
                    return ConversionCost.LoosingPrecision;

                case PhpTypeCode.PhpArray:
                    return ConversionCost.Warning;

                default:
                    return ConversionCost.NoConversion;
            }
        }

        public static ConversionCost ToPhpArray(PhpValue value)
        {
            switch (value.TypeCode)
            {
                case PhpTypeCode.Int32:
                case PhpTypeCode.Long:
                case PhpTypeCode.Double:
                case PhpTypeCode.Boolean:
                case PhpTypeCode.MutableString:
                case PhpTypeCode.String:
                    return ConversionCost.Warning;

                case PhpTypeCode.PhpArray:
                    return ConversionCost.Pass;

                default:
                    return ConversionCost.NoConversion;
            }
        }

        public static ConversionCost ToClass<T>(PhpValue value) where T : class
        {
            switch (value.TypeCode)
            {
                case PhpTypeCode.Null:
                    return ConversionCost.ImplicitCast;

                case PhpTypeCode.Object:
                    if (value.Object is T)
                    {
                        return ConversionCost.Pass;
                    }
                    else
                    {
                        Debug.Assert(!value.IsNull);
                        return ConversionCost.NoConversion;
                    }

                case PhpTypeCode.String:
                    if (typeof(T) == typeof(byte[])) // string -> byte[]
                    {
                        return ConversionCost.PassCostly;
                    }
                    return ConversionCost.NoConversion;
                case PhpTypeCode.MutableString:
                    if (typeof(T) == typeof(byte[])) // MutableString -> byte[]
                    {
                        return value.MutableString.ContainsBinaryData ? ConversionCost.Pass : ConversionCost.PassCostly;
                    }
                    return ConversionCost.NoConversion;

                case PhpTypeCode.Alias:
                    return ToClass<T>(value.Alias.Value);

                default:
                    return ConversionCost.NoConversion;
            }
        }

        public static ConversionCost ToIPhpCallable(object value)
        {
            return (value is IPhpCallable) ? ConversionCost.Pass : ConversionCost.NoConversion;
        }

        public static ConversionCost ToIPhpCallable(PhpValue value)
        {
            switch (value.TypeCode)
            {
                case PhpTypeCode.String:
                case PhpTypeCode.MutableString:
                case PhpTypeCode.PhpArray:
                    return ConversionCost.LoosingPrecision;

                case PhpTypeCode.Object:
                    return ToIPhpCallable(value.Object);

                case PhpTypeCode.Alias:
                    return ToIPhpCallable(value.Alias.Value);

                default:
                    return ConversionCost.NoConversion;
            }
        }

        public static ConversionCost ToIntPtr(PhpValue value)
        {
            // TODO: once we'll be able to store structs
            return ConversionCost.NoConversion;
        }

        #endregion
    }
}
