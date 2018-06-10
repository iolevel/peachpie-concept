﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Pchp.CodeAnalysis.Symbols
{
    #region CoreMethod, CoreConstructor, CoreOperator

    /// <summary>
    /// Descriptor of a well-known method.
    /// </summary>
    [DebuggerDisplay("CoreMethod {DebuggerDisplay,nq}")]
    class CoreMethod
    {
        #region Fields

        /// <summary>
        /// Lazily associated symbol.
        /// </summary>
        MethodSymbol _lazySymbol;

        /// <summary>
        /// Parametyer types.
        /// </summary>
        readonly CoreType[] _ptypes;

        /// <summary>
        /// Declaring class. Cannot be <c>null</c>.
        /// </summary>
        public readonly CoreType DeclaringClass;

        /// <summary>
        /// The method name.
        /// </summary>
        public readonly string MethodName;

        #endregion

        public CoreMethod(CoreType declaringClass, string methodName, params CoreType[] ptypes)
        {
            Contract.ThrowIfNull(declaringClass);
            Contract.ThrowIfNull(methodName);

            this.DeclaringClass = declaringClass;
            this.MethodName = methodName;

            _ptypes = ptypes;
        }

        /// <summary>
        /// Gets associated symbol.
        /// </summary>
        public MethodSymbol Symbol
        {
            get
            {
                var symbol = _lazySymbol;
                if (symbol == null)
                {
                    symbol = ResolveSymbol();
                    Contract.ThrowIfNull(symbol);

                    Interlocked.CompareExchange(ref _lazySymbol, symbol, null);
                }
                return symbol;
            }
        }

        string DebuggerDisplay => DeclaringClass.FullName + "." + MethodName;

        /// <summary>
        /// Implicit cast to method symbol.
        /// </summary>
        public static implicit operator MethodSymbol(CoreMethod m) => m.Symbol;

        #region ResolveSymbol

        /// <summary>
        /// Resolves <see cref="MethodSymbol"/> of this descriptor.
        /// </summary>
        protected virtual MethodSymbol ResolveSymbol()
        {
            var type = this.DeclaringClass.Symbol;
            if (type == null)
                throw new InvalidOperationException();

            var methods = type.GetMembers(MethodName);
            return methods.OfType<MethodSymbol>().First(MatchesSignature);
        }

        protected bool MatchesSignature(MethodSymbol m)
        {
            var ps = m.Parameters;
            if (ps.Length != _ptypes.Length)
                return false;

            for (int i = 0; i < ps.Length; i++)
                if (_ptypes[i] != ps[i].Type)
                    return false;

            return true;
        }

        #endregion
    }

    class CoreField
    {
        #region Fields

        /// <summary>
        /// Lazily associated symbol.
        /// </summary>
        FieldSymbol _lazySymbol;

        /// <summary>
        /// Declaring class. Cannot be <c>null</c>.
        /// </summary>
        public readonly CoreType DeclaringClass;

        /// <summary>
        /// The field name.
        /// </summary>
        public readonly string FieldName;

        #endregion

        public CoreField(CoreType declaringClass, string fldName)
        {
            Contract.ThrowIfNull(declaringClass);
            Contract.ThrowIfNull(fldName);

            this.DeclaringClass = declaringClass;
            this.FieldName = fldName;
        }

        /// <summary>
        /// Gets associated symbol.
        /// </summary>
        public FieldSymbol Symbol
        {
            get
            {
                var symbol = _lazySymbol;
                if (symbol == null)
                {
                    symbol = ResolveSymbol();
                    Contract.ThrowIfNull(symbol);

                    Interlocked.CompareExchange(ref _lazySymbol, symbol, null);
                }
                return symbol;
            }
        }

        string DebuggerDisplay => DeclaringClass.FullName + "." + FieldName;

        /// <summary>
        /// Implicit cast to field symbol.
        /// </summary>
        public static implicit operator FieldSymbol(CoreField m) => m.Symbol;

        #region ResolveSymbol

        /// <summary>
        /// Resolves <see cref="FieldSymbol"/> of this descriptor.
        /// </summary>
        protected virtual FieldSymbol ResolveSymbol()
        {
            var type = this.DeclaringClass.Symbol;
            if (type == null)
                throw new InvalidOperationException();

            var fields = type.GetMembers(FieldName);
            return fields.OfType<FieldSymbol>().First();
        }

        #endregion
    }

    class CoreProperty
    {
        #region Fields

        /// <summary>
        /// Lazily associated symbol.
        /// </summary>
        PropertySymbol _lazySymbol;

        /// <summary>
        /// Declaring class. Cannot be <c>null</c>.
        /// </summary>
        public readonly CoreType DeclaringClass;

        /// <summary>
        /// The field name.
        /// </summary>
        public readonly string PropertyName;

        #endregion

        public CoreProperty(CoreType declaringClass, string propertyName)
        {
            Contract.ThrowIfNull(declaringClass);
            Contract.ThrowIfNull(propertyName);

            this.DeclaringClass = declaringClass;
            this.PropertyName = propertyName;
        }

        /// <summary>
        /// Gets associated symbol.
        /// </summary>
        public PropertySymbol Symbol
        {
            get
            {
                var symbol = _lazySymbol;
                if (symbol == null)
                {
                    symbol = ResolveSymbol();
                    Contract.ThrowIfNull(symbol);

                    Interlocked.CompareExchange(ref _lazySymbol, symbol, null);
                }
                return symbol;
            }
        }

        public MethodSymbol Getter => Symbol.GetMethod;

        public MethodSymbol Setter => Symbol.SetMethod;

        string DebuggerDisplay => DeclaringClass.FullName + "." + PropertyName;

        /// <summary>
        /// Implicit cast to field symbol.
        /// </summary>
        public static implicit operator PropertySymbol(CoreProperty m) => m.Symbol;

        #region ResolveSymbol

        /// <summary>
        /// Resolves <see cref="FieldSymbol"/> of this descriptor.
        /// </summary>
        protected virtual PropertySymbol ResolveSymbol()
        {
            var type = this.DeclaringClass.Symbol;
            if (type == null)
                throw new InvalidOperationException();

            var fields = type.GetMembers(PropertyName);
            return fields.OfType<PropertySymbol>().First();
        }

        #endregion
    }

    /// <summary>
    /// Descriptor of a well-known constructor.
    /// </summary>
    class CoreConstructor : CoreMethod
    {
        public CoreConstructor(CoreType declaringClass, params CoreType[] ptypes)
            : base(declaringClass, ".ctor", ptypes)
        {

        }

        protected override MethodSymbol ResolveSymbol()
        {
            var type = this.DeclaringClass.Symbol;
            if (type == null)
                throw new InvalidOperationException();

            var methods = type.InstanceConstructors;
            return methods.First(MatchesSignature);
        }
    }

    /// <summary>
    /// Descriptor of a well-known operator method.
    /// </summary>
    class CoreOperator : CoreMethod
    {
        /// <summary>
        /// Creates the descriptor.
        /// </summary>
        /// <param name="declaringClass">Containing class.</param>
        /// <param name="name">Operator name, without <c>op_</c> prefix.</param>
        /// <param name="ptypes">CLR parameters.</param>
        public CoreOperator(CoreType declaringClass, string name, params CoreType[] ptypes)
            : base(declaringClass, name, ptypes)
        {
            Debug.Assert(name.StartsWith("op_"));
        }

        protected override MethodSymbol ResolveSymbol()
        {
            var type = this.DeclaringClass.Symbol;
            if (type == null)
                throw new InvalidOperationException();

            var methods = type.GetMembers(this.MethodName);
            return methods.OfType<MethodSymbol>()
                .Where(m => m.HasSpecialName)
                .First(MatchesSignature);
        }
    }

    class CoreExplicitCast : CoreMethod
    {
        readonly CoreType _castTo;

        public CoreExplicitCast(CoreType declaringClass, CoreType castTo)
            : base(declaringClass, WellKnownMemberNames.ExplicitConversionName, declaringClass)
        {
            _castTo = castTo;
        }

        protected override MethodSymbol ResolveSymbol()
        {
            var type = this.DeclaringClass.Symbol;
            if (type == null)
                throw new InvalidOperationException();

            var methods = type.GetMembers(this.MethodName);
            return methods.OfType<MethodSymbol>()
                .Where(m => m.HasSpecialName && m.IsStatic && m.ParameterCount == 1 && m.Parameters[0].Type == type && m.ReturnType == _castTo)
                .First();
        }
    }

    #endregion

    /// <summary>
    /// Set of well-known methods declared in a core library.
    /// </summary>
    class CoreMethods
    {
        public readonly PhpValueHolder PhpValue;
        public readonly PhpAliasHolder PhpAlias;
        public readonly PhpArrayHolder PhpArray;
        public readonly IPhpArrayHolder IPhpArray;
        public readonly IPhpConvertibleHolder IPhpConvertible;
        public readonly PhpNumberHolder PhpNumber;
        public readonly OperatorsHolder Operators;
        public readonly PhpStringHolder PhpString;
        public readonly PhpStringBlobHolder PhpStringBlob;
        public readonly ConstructorsHolder Ctors;
        public readonly ContextHolder Context;
        public readonly DynamicHolder Dynamic;
        public readonly ReflectionHolder Reflection;

        public CoreMethods(CoreTypes types)
        {
            Contract.ThrowIfNull(types);

            PhpValue = new PhpValueHolder(types);
            PhpAlias = new PhpAliasHolder(types);
            PhpArray = new PhpArrayHolder(types);
            IPhpArray = new IPhpArrayHolder(types);
            IPhpConvertible = new IPhpConvertibleHolder(types);
            PhpNumber = new PhpNumberHolder(types);
            PhpString = new PhpStringHolder(types);
            PhpStringBlob = new PhpStringBlobHolder(types);
            Operators = new OperatorsHolder(types);
            Ctors = new ConstructorsHolder(types);
            Context = new ContextHolder(types);
            Dynamic = new DynamicHolder(types);
            Reflection = new ReflectionHolder(types);
        }

        public struct OperatorsHolder
        {
            public OperatorsHolder(CoreTypes ct)
            {
                SetValue_PhpValueRef_PhpValue = ct.Operators.Method("SetValue", ct.PhpValue, ct.PhpValue);
                EnsureObject_ObjectRef = ct.Operators.Method("EnsureObject", ct.Object);
                EnsureArray_PhpArrayRef = ct.Operators.Method("EnsureArray", ct.PhpArray);
                EnsureArray_IPhpArrayRef = ct.Operators.Method("EnsureArray", ct.IPhpArray);
                EnsureArray_ArrayAccess = ct.Operators.Method("EnsureArray", ct.ArrayAccess);
                EnsureArray_Object = ct.Operators.Method("EnsureArray", ct.Object);
                GetItemValue_String_IntStringKey = ct.Operators.Method("GetItemValue", ct.String, ct.IntStringKey);
                GetItemValueOrNull_String_IntStringKey = ct.Operators.Method("GetItemValueOrNull", ct.String, ct.IntStringKey);
                GetItemValue_String_PhpValue_Bool = ct.Operators.Method("GetItemValue", ct.String, ct.PhpValue, ct.Boolean);
                GetItemValue_String_Int = ct.Operators.Method("GetItemValue", ct.String, ct.Int32);
                GetItemValue_PhpValue_PhpValue_Bool = ct.Operators.Method("GetItemValue", ct.PhpValue, ct.PhpValue, ct.Boolean);
                EnsureItemAlias_PhpValue_PhpValue_Bool = ct.Operators.Method("EnsureItemAlias", ct.PhpValue, ct.PhpValue, ct.Boolean);
                EnsureItemAlias_IPhpArray_PhpValue_Bool = ct.Operators.Method("EnsureItemAlias", ct.IPhpArray, ct.PhpValue, ct.Boolean);
                EnsureItemArray_IPhpArray_PhpValue = ct.Operators.Method("EnsureItemArray", ct.IPhpArray, ct.PhpValue);
                EnsureItemObject_IPhpArray_PhpValue = ct.Operators.Method("EnsureItemObject", ct.IPhpArray, ct.PhpValue);
                IsSet_PhpValue = ct.Operators.Method("IsSet", ct.PhpValue);
                IsEmpty_PhpValue = ct.Operators.Method("IsEmpty", ct.PhpValue);
                IsNullOrEmpty_String = ct.String.Method("IsNullOrEmpty", ct.String);
                Concat_String_String = ct.String.Method("Concat", ct.String, ct.String);

                ToString_Bool = ct.Convert.Method("ToString", ct.Boolean);
                ToString_Int32 = ct.Convert.Method("ToString", ct.Int32);
                ToString_Long = ct.Convert.Method("ToString", ct.Long);
                ToString_Double_Context = ct.Convert.Method("ToString", ct.Double, ct.Context);
                Long_ToString = ct.Long.Method("ToString");
                ToChar_String = ct.Convert.Method("ToChar", ct.String);
                ToBoolean_String = ct.Convert.Method("ToBoolean", ct.String);
                ToBoolean_PhpValue = new CoreExplicitCast(ct.PhpValue, ct.Boolean);
                ToBoolean_Object = ct.Convert.Method("ToBoolean", ct.Object);
                ToBoolean_IPhpConvertible = ct.Convert.Method("ToBoolean", ct.IPhpConvertible);
                ToLong_PhpValue = new CoreExplicitCast(ct.PhpValue, ct.Long);
                ToDouble_PhpValue = new CoreExplicitCast(ct.PhpValue, ct.Double);
                ToNumber_PhpValue = ct.Convert.Method("ToNumber", ct.PhpValue);
                ToNumber_String = ct.Convert.Method("ToNumber", ct.String);
                ToLong_String = ct.Convert.Method("StringToLongInteger", ct.String);
                ToDouble_String = ct.Convert.Method("StringToDouble", ct.String);

                AsObject_PhpValue = ct.Convert.Method("AsObject", ct.PhpValue);
                AsArray_PhpValue = ct.Convert.Method("AsArray", ct.PhpValue);
                ToArray_PhpValue = ct.Convert.Method("ToArray", ct.PhpValue);
                ToPhpString_PhpValue_Context = ct.Convert.Method("ToPhpString", ct.PhpValue, ct.Context);
                ToClass_PhpValue = ct.Convert.Method("ToClass", ct.PhpValue);
                ToClass_IPhpArray = ct.Convert.Method("ToClass", ct.IPhpArray);
                AsCallable_PhpValue_RuntimeTypeHandle = ct.Convert.Method("AsCallable", ct.PhpValue, ct.RuntimeTypeHandle);
                AsCallable_String_RuntimeTypeHandle = ct.Convert.Method("AsCallable", ct.String, ct.RuntimeTypeHandle);
                GetArrayAccess_PhpValue = ct.Operators.Method("GetArrayAccess", ct.PhpValue);
                IsInstanceOf_Object_PhpTypeInfo = ct.Convert.Method("IsInstanceOf", ct.Object, ct.PhpTypeInfo);
                ToIntStringKey_PhpValue = ct.Convert.Method("ToIntStringKey", ct.PhpValue);

                Echo_String = ct.Context.Method("Echo", ct.String);
                Echo_PhpString = ct.Context.Method("Echo", ct.PhpString);
                Echo_PhpNumber = ct.Context.Method("Echo", ct.PhpNumber);
                Echo_PhpValue = ct.Context.Method("Echo", ct.PhpValue);
                Echo_Object = ct.Context.Method("Echo", ct.Object);
                Echo_Double = ct.Context.Method("Echo", ct.Double);
                Echo_Long = ct.Context.Method("Echo", ct.Long);
                Echo_Int32 = ct.Context.Method("Echo", ct.Int32);
                Echo_Bool = ct.Context.Method("Echo", ct.Boolean);

                GetForeachEnumerator_PhpValue_Bool_RuntimeTypeHandle = ct.Operators.Method("GetForeachEnumerator", ct.PhpValue, ct.Boolean, ct.RuntimeTypeHandle);
                GetForeachEnumerator_Iterator = ct.Operators.Method("GetForeachEnumerator", ct.Iterator);

                GetSelf_RuntimeTypeHandle = ct.Operators.Method("GetSelf", ct.RuntimeTypeHandle);
                GetSelfOrNull_RuntimeTypeHandle = ct.Operators.Method("GetSelfOrNull", ct.RuntimeTypeHandle);
                GetParent_RuntimeTypeHandle = ct.Operators.Method("GetParent", ct.RuntimeTypeHandle);
                GetParent_PhpTypeInfo = ct.Operators.Method("GetParent", ct.PhpTypeInfo);
                TypeNameOrObjectToType_Context_PhpValue = ct.Operators.Method("TypeNameOrObjectToType", ct.Context, ct.PhpValue);

                Clone_Context_Object = ct.Operators.Method("Clone", ct.Context, ct.Object);
                Eval_Context_PhpArray_object_RuntimeTypeHandle_string_string_int_int = ct.Operators.Method("Eval", ct.Context, ct.PhpArray, ct.Object, ct.RuntimeTypeHandle, ct.String, ct.String, ct.Int32, ct.Int32);
                GetName_PhpTypeInfo = ct.PhpTypeInfo.Property("Name");
                GetTypeHandle_PhpTypeInfo = ct.PhpTypeInfo.Property("TypeHandle");

                BuildClosure_Context_IPhpCallable_Object_RuntimeTypeHandle_PhpTypeInfo_PhpArray_PhpArray = ct.Operators.Method("BuildClosure", ct.Context, ct.IPhpCallable, ct.Object, ct.RuntimeTypeHandle, ct.PhpTypeInfo, ct.PhpArray, ct.PhpArray);
                This_Closure = ct.Operators.Method("This", ct.Closure);
                Scope_Closure = ct.Operators.Method("Scope", ct.Closure);
                Static_Closure = ct.Operators.Method("Static", ct.Closure);
                Context_Closure = ct.Operators.Method("Context", ct.Closure);

                BuildGenerator_Context_Object_PhpArray_PhpArray_GeneratorStateMachineDelegate_RuntimeMethodHandle = ct.Operators.Method("BuildGenerator", ct.Context, ct.Object, ct.PhpArray, ct.PhpArray, ct.GeneratorStateMachineDelegate, ct.RuntimeMethodHandle);
                GetGeneratorState_Generator = ct.Operators.Method("GetGeneratorState", ct.Generator);
                SetGeneratorState_Generator_int = ct.Operators.Method("SetGeneratorState", ct.Generator, ct.Int32);
                HandleGeneratorException_Generator = ct.Operators.Method("HandleGeneratorException", ct.Generator);
                SetGeneratorCurrent_Generator_PhpValue = ct.Operators.Method("SetGeneratorCurrent", ct.Generator, ct.PhpValue);
                SetGeneratorCurrent_Generator_PhpValue_PhpValue = ct.Operators.Method("SetGeneratorCurrent", ct.Generator, ct.PhpValue, ct.PhpValue);
                SetGeneratorCurrentFrom_Generator_PhpValue_PhpValue = ct.Operators.Method("SetGeneratorCurrentFrom", ct.Generator, ct.PhpValue, ct.PhpValue);
                GetGeneratorSentItem_Generator = ct.Operators.Method("GetGeneratorSentItem", ct.Generator);
                SetGeneratorReturnedValue_Generator_PhpValue = ct.Operators.Method("SetGeneratorReturnedValue", ct.Generator, ct.PhpValue);

                offsetGet_ArrayAccess_PhpValue = ct.ArrayAccess.Method("offsetGet", ct.PhpValue);
                offsetSet_ArrayAccess_PhpValue_PhpValue = ct.ArrayAccess.Method("offsetSet", ct.PhpValue, ct.PhpValue);
                offsetUnset_ArrayAccess_PhpValue = ct.ArrayAccess.Method("offsetUnset", ct.PhpValue);

                ReadConstant_Context_String_Int = ct.Operators.Method("ReadConstant", ct.Context, ct.String, ct.Int32);
                ReadConstant_Context_String_Int_String = ct.Operators.Method("ReadConstant", ct.Context, ct.String, ct.Int32, ct.String);
                DeclareConstant_Context_string_int_PhpValue = ct.Operators.Method("DeclareConstant", ct.Context, ct.String, ct.Int32, ct.PhpValue);

                Ceq_long_double = ct.Comparison.Method("Ceq", ct.Long, ct.Double);
                Ceq_long_bool = ct.Comparison.Method("Ceq", ct.Long, ct.Boolean);
                Ceq_long_string = ct.Comparison.Method("Ceq", ct.Long, ct.String);
                Ceq_double_string = ct.Comparison.Method("Ceq", ct.Double, ct.String);
                Ceq_string_long = ct.Comparison.Method("Ceq", ct.String, ct.Long);
                Ceq_string_double = ct.Comparison.Method("Ceq", ct.String, ct.Double);
                Ceq_string_bool = ct.Comparison.Method("Ceq", ct.String, ct.Boolean);
                CeqNull_value = ct.Comparison.Method("CeqNull", ct.PhpValue);
                Clt_long_double = ct.Comparison.Method("Clt", ct.Long, ct.Double);
                Cgt_long_double = ct.Comparison.Method("Cgt", ct.Long, ct.Double);
                Compare_bool_bool = ct.Comparison.Method("Compare", ct.Boolean, ct.Boolean);
                Compare_number_value = ct.Comparison.Method("Compare", ct.PhpNumber, ct.PhpValue);
                Compare_long_value = ct.Comparison.Method("Compare", ct.Long, ct.PhpValue);
                Compare_value_long = ct.Comparison.Method("Compare", ct.PhpValue, ct.Long);
                Compare_double_value = ct.Comparison.Method("Compare", ct.Double, ct.PhpValue);
                Compare_bool_value = ct.Comparison.Method("Compare", ct.Boolean, ct.PhpValue);
                Compare_value_value = ct.Comparison.Method("Compare", ct.PhpValue, ct.PhpValue);
                Compare_string_string = ct.Comparison.Method("Compare", ct.String, ct.String);
                Compare_string_long = ct.Comparison.Method("Compare", ct.String, ct.Long);
                Compare_string_double = ct.Comparison.Method("Compare", ct.String, ct.Double);
                Compare_string_value = ct.Comparison.Method("Compare", ct.String, ct.PhpValue);
                CompareNull_value = ct.Comparison.Method("CompareNull", ct.PhpValue);

                StrictCeq_bool_PhpValue = ct.StrictComparison.Method("Ceq", ct.Boolean, ct.PhpValue);
                StrictCeq_long_PhpValue = ct.StrictComparison.Method("Ceq", ct.Long, ct.PhpValue);
                StrictCeq_long_PhpNumber = ct.StrictComparison.Method("Ceq", ct.Long, ct.PhpNumber);
                StrictCeq_double_PhpValue = ct.StrictComparison.Method("Ceq", ct.Double, ct.PhpValue);
                StrictCeq_double_PhpNumber = ct.StrictComparison.Method("Ceq", ct.Double, ct.PhpNumber);
                StrictCeq_PhpValue_PhpValue = ct.StrictComparison.Method("Ceq", ct.PhpValue, ct.PhpValue);
                StrictCeq_PhpValue_bool = ct.StrictComparison.Method("Ceq", ct.PhpValue, ct.Boolean);

                Div_PhpValue_PhpValue = ct.PhpValue.Method(WellKnownMemberNames.DivisionOperatorName, ct.PhpValue, ct.PhpValue);
                Div_long_PhpValue = ct.PhpValue.Method(WellKnownMemberNames.DivisionOperatorName, ct.Long, ct.PhpValue);
                Div_double_PhpValue = ct.PhpValue.Method(WellKnownMemberNames.DivisionOperatorName, ct.Double, ct.PhpValue);
                BitwiseOr_PhpValue_PhpValue = ct.PhpValue.Method(WellKnownMemberNames.BitwiseOrOperatorName, ct.PhpValue, ct.PhpValue);
                BitwiseAnd_PhpValue_PhpValue = ct.PhpValue.Method(WellKnownMemberNames.BitwiseAndOperatorName, ct.PhpValue, ct.PhpValue);
                BitwiseXor_PhpValue_PhpValue = ct.PhpValue.Method(WellKnownMemberNames.ExclusiveOrOperatorName, ct.PhpValue, ct.PhpValue);
                BitwiseNot_PhpValue = ct.PhpValue.Method(WellKnownMemberNames.OnesComplementOperatorName, ct.PhpValue);
            }

            public readonly CoreMethod
                SetValue_PhpValueRef_PhpValue,
                EnsureObject_ObjectRef, EnsureArray_PhpArrayRef, EnsureArray_IPhpArrayRef, EnsureArray_ArrayAccess, EnsureArray_Object,
                GetItemValue_String_IntStringKey, GetItemValueOrNull_String_IntStringKey, GetItemValue_String_PhpValue_Bool, GetItemValue_String_Int, GetItemValue_PhpValue_PhpValue_Bool,
                EnsureItemAlias_IPhpArray_PhpValue_Bool, EnsureItemAlias_PhpValue_PhpValue_Bool,
                EnsureItemArray_IPhpArray_PhpValue,
                EnsureItemObject_IPhpArray_PhpValue,
                IsSet_PhpValue, IsEmpty_PhpValue, IsNullOrEmpty_String, Concat_String_String,
                ToString_Bool, ToString_Long, ToString_Int32, ToString_Double_Context, Long_ToString,
                ToChar_String,
                ToBoolean_String, ToBoolean_PhpValue, ToBoolean_Object, ToBoolean_IPhpConvertible,
                ToLong_PhpValue, ToDouble_PhpValue, ToLong_String, ToDouble_String,
                ToNumber_PhpValue, ToNumber_String,
                AsObject_PhpValue, AsArray_PhpValue, ToArray_PhpValue, GetArrayAccess_PhpValue, ToPhpString_PhpValue_Context, ToClass_PhpValue, ToClass_IPhpArray, AsCallable_PhpValue_RuntimeTypeHandle, AsCallable_String_RuntimeTypeHandle,
                IsInstanceOf_Object_PhpTypeInfo,
                ToIntStringKey_PhpValue,
                Echo_Object, Echo_String, Echo_PhpString, Echo_PhpNumber, Echo_PhpValue, Echo_Double, Echo_Long, Echo_Int32, Echo_Bool,

                GetForeachEnumerator_PhpValue_Bool_RuntimeTypeHandle,
                GetForeachEnumerator_Iterator,

                GetSelf_RuntimeTypeHandle, GetSelfOrNull_RuntimeTypeHandle, GetParent_RuntimeTypeHandle, GetParent_PhpTypeInfo,
                TypeNameOrObjectToType_Context_PhpValue,

                Clone_Context_Object,
                Eval_Context_PhpArray_object_RuntimeTypeHandle_string_string_int_int,

                BuildClosure_Context_IPhpCallable_Object_RuntimeTypeHandle_PhpTypeInfo_PhpArray_PhpArray,
                This_Closure, Scope_Closure, Static_Closure, Context_Closure,

                BuildGenerator_Context_Object_PhpArray_PhpArray_GeneratorStateMachineDelegate_RuntimeMethodHandle,
                GetGeneratorState_Generator, SetGeneratorState_Generator_int, HandleGeneratorException_Generator,
                SetGeneratorCurrent_Generator_PhpValue, SetGeneratorCurrent_Generator_PhpValue_PhpValue, SetGeneratorCurrentFrom_Generator_PhpValue_PhpValue,
                GetGeneratorSentItem_Generator, SetGeneratorReturnedValue_Generator_PhpValue,

                offsetGet_ArrayAccess_PhpValue, offsetSet_ArrayAccess_PhpValue_PhpValue, offsetUnset_ArrayAccess_PhpValue,

                ReadConstant_Context_String_Int,
                ReadConstant_Context_String_Int_String,
                DeclareConstant_Context_string_int_PhpValue,

                Ceq_long_double, Ceq_long_bool, Ceq_long_string, Ceq_double_string, Ceq_string_long, Ceq_string_double, Ceq_string_bool, CeqNull_value,
                Clt_long_double, Cgt_long_double,
                Compare_bool_bool, Compare_number_value,
                Compare_long_value, Compare_value_long, Compare_value_value, Compare_double_value, Compare_bool_value, Compare_string_string, Compare_string_long, Compare_string_double, Compare_string_value,
                CompareNull_value,

                StrictCeq_bool_PhpValue, StrictCeq_long_PhpValue, StrictCeq_long_PhpNumber, StrictCeq_double_PhpValue, StrictCeq_double_PhpNumber, StrictCeq_PhpValue_PhpValue,
                StrictCeq_PhpValue_bool,

                Div_PhpValue_PhpValue, Div_long_PhpValue, Div_double_PhpValue,
                BitwiseAnd_PhpValue_PhpValue, BitwiseOr_PhpValue_PhpValue, BitwiseXor_PhpValue_PhpValue, BitwiseNot_PhpValue;

            public readonly CoreProperty
                GetName_PhpTypeInfo, GetTypeHandle_PhpTypeInfo;
        }

        public struct PhpValueHolder
        {
            public PhpValueHolder(CoreTypes ct)
            {
                ToBoolean = ct.PhpValue.Method("ToBoolean");
                ToLong = ct.PhpValue.Method("ToLong");
                ToDouble = ct.PhpValue.Method("ToDouble");
                ToString_Context = ct.PhpValue.Method("ToString", ct.Context);
                ToClass = ct.PhpValue.Method("ToClass");
                EnsureObject = ct.PhpValue.Method("EnsureObject");
                EnsureArray = ct.PhpValue.Method("EnsureArray");
                EnsureAlias = ct.PhpValue.Method("EnsureAlias");

                Eq_PhpValue_PhpValue = ct.PhpValue.Operator(WellKnownMemberNames.EqualityOperatorName, ct.PhpValue, ct.PhpValue);
                Eq_PhpValue_String = ct.PhpValue.Operator(WellKnownMemberNames.EqualityOperatorName, ct.PhpValue, ct.String);

                IsEmpty = ct.PhpValue.Property("IsEmpty");

                DeepCopy = ct.PhpValue.Method("DeepCopy");
                GetValue = ct.PhpValue.Method("GetValue");
                PassValue = ct.PhpValue.Method("PassValue");
                ToArray = ct.PhpValue.Method("ToArray");
                AsObject = ct.PhpValue.Method("AsObject");

                get_Long = ct.PhpValue.Method("get_Long");   // TODO: special name, property
                get_Double = ct.PhpValue.Method("get_Double");   // TODO: special name, property
                get_Boolean = ct.PhpValue.Method("get_Boolean");   // TODO: special name, property
                get_String = ct.PhpValue.Method("get_String");   // TODO: special name, property
                Object = ct.PhpValue.Property("Object");
                get_Array = ct.PhpValue.Method("get_Array");   // TODO: special name, property

                Create_Boolean = ct.PhpValue.Method("Create", ct.Boolean);
                Create_Long = ct.PhpValue.Method("Create", ct.Long);
                Create_Int = ct.PhpValue.Method("Create", ct.Int32);
                Create_Double = ct.PhpValue.Method("Create", ct.Double);
                Create_String = ct.PhpValue.Method("Create", ct.String);
                Create_PhpString = ct.PhpValue.Method("Create", ct.PhpString);
                Create_PhpNumber = ct.PhpValue.Method("Create", ct.PhpNumber);
                Create_PhpArray = ct.PhpValue.Method("Create", ct.PhpArray);
                Create_PhpAlias = ct.PhpValue.Method("Create", ct.PhpAlias);
                Create_IntStringKey = ct.PhpValue.Method("Create", ct.IntStringKey);

                FromClr_Object = ct.PhpValue.Method("FromClr", ct.Object);
                FromClass_Object = ct.PhpValue.Method("FromClass", ct.Object);

                Void = ct.PhpValue.Field("Void");
                Null = ct.PhpValue.Field("Null");
            }

            public readonly CoreMethod
                ToLong, ToDouble, ToBoolean, ToString_Context, ToClass, EnsureObject, EnsureArray, EnsureAlias, ToArray,
                AsObject,
                DeepCopy, GetValue, PassValue,
                Eq_PhpValue_PhpValue, Eq_PhpValue_String,
                get_Long, get_Double, get_Boolean, get_String, get_Array,
                Create_Boolean, Create_Long, Create_Int, Create_Double, Create_String, Create_PhpString, Create_PhpNumber, Create_PhpAlias, Create_PhpArray, Create_IntStringKey,
                FromClr_Object, FromClass_Object;

            public readonly CoreField
                Void, Null;

            public readonly CoreProperty
                IsEmpty, Object;

        }

        public struct PhpAliasHolder
        {
            public PhpAliasHolder(CoreTypes ct)
            {
                _value = null;

                EnsureObject = ct.PhpAlias.Method("EnsureObject");
                EnsureArray = ct.PhpAlias.Method("EnsureArray");
            }

            public readonly CoreMethod
                EnsureObject, EnsureArray;

            /// <summary>
            /// Lazily gets <c>PhpAlias.Value</c> field.
            /// </summary>
            public FieldSymbol Value
            {
                get
                {
                    if (_value == null)
                    {
                        Debug.Assert(EnsureObject.DeclaringClass.Symbol != null);
                        _value = (EnsureObject.DeclaringClass.Symbol)
                            .GetMembers("Value").OfType<FieldSymbol>().First();
                        Debug.Assert(_value != null);
                    }
                    return _value;
                }
            }
            FieldSymbol _value;
        }

        public struct PhpNumberHolder
        {
            public PhpNumberHolder(CoreTypes ct)
            {
                ToBoolean = ct.PhpNumber.Method("ToBoolean");
                ToLong = ct.PhpNumber.Method("ToLong");
                ToDouble = ct.PhpNumber.Method("ToDouble");
                ToString_Context = ct.PhpNumber.Method("ToString", ct.Context);
                ToClass = ct.PhpNumber.Method("ToClass");

                CompareTo_number = ct.PhpNumber.Method("CompareTo", ct.PhpNumber);
                CompareTo_long = ct.PhpNumber.Method("CompareTo", ct.Long);
                CompareTo_double = ct.PhpNumber.Method("CompareTo", ct.Double);

                Create_Long = ct.PhpNumber.Method("Create", ct.Long);
                Create_Double = ct.PhpNumber.Method("Create", ct.Double);
                Default = ct.PhpNumber.Field("Default");

                get_Long = ct.PhpNumber.Method("get_Long");   // TODO: special name, property
                get_Double = ct.PhpNumber.Method("get_Double");   // TODO: special name, property

                Eq_number_PhpValue = ct.PhpNumber.Operator(WellKnownMemberNames.EqualityOperatorName, ct.PhpNumber, ct.PhpValue);
                Ineq_number_PhpValue = ct.PhpNumber.Operator(WellKnownMemberNames.InequalityOperatorName, ct.PhpNumber, ct.PhpValue);
                Eq_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.EqualityOperatorName, ct.PhpNumber, ct.PhpNumber);
                Ineq_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.InequalityOperatorName, ct.PhpNumber, ct.PhpNumber);
                Eq_number_long = ct.PhpNumber.Operator(WellKnownMemberNames.EqualityOperatorName, ct.PhpNumber, ct.Long);
                Ineq_number_long = ct.PhpNumber.Operator(WellKnownMemberNames.InequalityOperatorName, ct.PhpNumber, ct.Long);
                Eq_number_double = ct.PhpNumber.Operator(WellKnownMemberNames.EqualityOperatorName, ct.PhpNumber, ct.Double);
                Ineq_number_double = ct.PhpNumber.Operator(WellKnownMemberNames.InequalityOperatorName, ct.PhpNumber, ct.Double);
                Eq_long_number = ct.PhpNumber.Operator(WellKnownMemberNames.EqualityOperatorName, ct.Long, ct.PhpNumber);
                Ineq_long_number = ct.PhpNumber.Operator(WellKnownMemberNames.InequalityOperatorName, ct.Long, ct.PhpNumber);

                Add_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.AdditionOperatorName, ct.PhpNumber, ct.PhpNumber);
                Add_number_long = ct.PhpNumber.Operator(WellKnownMemberNames.AdditionOperatorName, ct.PhpNumber, ct.Long);
                Add_long_number = ct.PhpNumber.Operator(WellKnownMemberNames.AdditionOperatorName, ct.Long, ct.PhpNumber);
                Add_double_number = ct.PhpNumber.Operator(WellKnownMemberNames.AdditionOperatorName, ct.Double, ct.PhpNumber);
                Add_number_double = ct.PhpNumber.Operator(WellKnownMemberNames.AdditionOperatorName, ct.PhpNumber, ct.Double);
                Add_number_value = ct.PhpNumber.Operator(WellKnownMemberNames.AdditionOperatorName, ct.PhpNumber, ct.PhpValue);
                Add_value_number = ct.PhpNumber.Operator(WellKnownMemberNames.AdditionOperatorName, ct.PhpValue, ct.PhpNumber);
                Add_long_long = ct.PhpNumber.Method("Add", ct.Long, ct.Long);
                Add_long_double = ct.PhpNumber.Method("Add", ct.Long, ct.Double);
                Add_value_long = ct.PhpNumber.Method("Add", ct.PhpValue, ct.Long);
                Add_value_double = ct.PhpNumber.Method("Add", ct.PhpValue, ct.Double);
                Add_long_value = ct.PhpNumber.Method("Add", ct.Long, ct.PhpValue);
                Add_double_value = ct.PhpNumber.Method("Add", ct.Double, ct.PhpValue);
                Add_value_value = ct.PhpNumber.Method("Add", ct.PhpValue, ct.PhpValue);
                Add_value_array = ct.PhpNumber.Method("Add", ct.PhpValue, ct.PhpArray);
                Add_array_value = ct.PhpNumber.Method("Add", ct.PhpArray, ct.PhpValue);

                Subtract_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.SubtractionOperatorName, ct.PhpNumber, ct.PhpNumber);
                Subtract_long_number = ct.PhpNumber.Operator(WellKnownMemberNames.SubtractionOperatorName, ct.Long, ct.PhpNumber);
                Subtract_number_long = ct.PhpNumber.Operator(WellKnownMemberNames.SubtractionOperatorName, ct.PhpNumber, ct.Long);
                Subtract_long_long = ct.PhpNumber.Method("Sub", ct.Long, ct.Long);
                Subtract_number_double = ct.PhpNumber.Method("Sub", ct.PhpNumber, ct.Double);
                Subtract_long_double = ct.PhpNumber.Method("Sub", ct.Long, ct.Double);
                Subtract_value_value = ct.PhpNumber.Method("Sub", ct.PhpValue, ct.PhpValue);
                Subtract_value_long = ct.PhpNumber.Method("Sub", ct.PhpValue, ct.Long);
                Subtract_value_double = ct.PhpNumber.Method("Sub", ct.PhpValue, ct.Double);
                Subtract_value_number = ct.PhpNumber.Method("Sub", ct.PhpValue, ct.PhpNumber);
                Subtract_number_value = ct.PhpNumber.Method("Sub", ct.PhpNumber, ct.PhpValue);
                Subtract_long_value = ct.PhpNumber.Method("Sub", ct.Long, ct.PhpValue);
                Subtract_double_value = ct.PhpNumber.Method("Sub", ct.Double, ct.PhpValue);

                Negation = ct.PhpNumber.Operator(WellKnownMemberNames.UnaryNegationOperatorName, ct.PhpNumber);
                Negation_long = ct.PhpNumber.Method("Minus", ct.Long);

                Division_number_value = ct.PhpNumber.Operator(WellKnownMemberNames.DivisionOperatorName, ct.PhpNumber, ct.PhpValue);
                Division_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.DivisionOperatorName, ct.PhpNumber, ct.PhpNumber);
                Division_number_long = ct.PhpNumber.Operator(WellKnownMemberNames.DivisionOperatorName, ct.PhpNumber, ct.Long);
                Division_number_double = ct.PhpNumber.Operator(WellKnownMemberNames.DivisionOperatorName, ct.PhpNumber, ct.Double);
                Division_long_number = ct.PhpNumber.Operator(WellKnownMemberNames.DivisionOperatorName, ct.Long, ct.PhpNumber);

                Mul_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.MultiplyOperatorName, ct.PhpNumber, ct.PhpNumber);
                Mul_number_double = ct.PhpNumber.Operator(WellKnownMemberNames.MultiplyOperatorName, ct.PhpNumber, ct.Double);
                Mul_number_long = ct.PhpNumber.Operator(WellKnownMemberNames.MultiplyOperatorName, ct.PhpNumber, ct.Long);
                Mul_long_number = ct.PhpNumber.Operator(WellKnownMemberNames.MultiplyOperatorName, ct.Long, ct.PhpNumber);
                Mul_number_value = ct.PhpNumber.Operator(WellKnownMemberNames.MultiplyOperatorName, ct.PhpNumber, ct.PhpValue);
                Mul_value_number = ct.PhpNumber.Operator(WellKnownMemberNames.MultiplyOperatorName, ct.PhpValue, ct.PhpNumber);
                Mul_long_long = ct.PhpNumber.Method("Multiply", ct.Long, ct.Long);
                Mul_long_double = ct.PhpNumber.Method("Multiply", ct.Long, ct.Double);
                Mul_double_value = ct.PhpNumber.Method("Multiply", ct.Double, ct.PhpValue);
                Mul_long_value = ct.PhpNumber.Method("Multiply", ct.Long, ct.PhpValue);
                Mul_value_value = ct.PhpNumber.Method("Multiply", ct.PhpValue, ct.PhpValue);
                Mul_value_long = ct.PhpNumber.Method("Multiply", ct.PhpValue, ct.Long);
                Mul_value_double = ct.PhpNumber.Method("Multiply", ct.PhpValue, ct.Double);

                Pow_value_value = ct.PhpNumber.Method("Pow", ct.PhpValue, ct.PhpValue);
                Pow_number_number = ct.PhpNumber.Method("Pow", ct.PhpNumber, ct.PhpNumber);
                Pow_number_double = ct.PhpNumber.Method("Pow", ct.PhpNumber, ct.Double);
                Pow_number_value = ct.PhpNumber.Method("Pow", ct.PhpNumber, ct.PhpValue);
                Pow_double_double = ct.PhpNumber.Method("Pow", ct.Double, ct.Double);
                Pow_double_value = ct.PhpNumber.Method("Pow", ct.Double, ct.PhpValue);
                Pow_long_long = ct.PhpNumber.Method("Pow", ct.Long, ct.Long);
                Pow_long_double = ct.PhpNumber.Method("Pow", ct.Long, ct.Double);
                Pow_long_number = ct.PhpNumber.Method("Pow", ct.Long, ct.PhpNumber);
                Pow_long_value = ct.PhpNumber.Method("Pow", ct.Long, ct.PhpValue);

                Mod_value_value = ct.PhpNumber.Method("Mod", ct.PhpValue, ct.PhpValue);
                Mod_value_long = ct.PhpNumber.Method("Mod", ct.PhpValue, ct.Long);
                Mod_long_long = ct.PhpNumber.Method("Mod", ct.Long, ct.Long);
                Mod_long_value = ct.PhpNumber.Method("Mod", ct.Long, ct.PhpValue);

                gt_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.GreaterThanOperatorName, ct.PhpNumber, ct.PhpNumber);
                gt_number_long = ct.PhpNumber.Operator(WellKnownMemberNames.GreaterThanOperatorName, ct.PhpNumber, ct.Long);
                gt_number_double = ct.PhpNumber.Operator(WellKnownMemberNames.GreaterThanOperatorName, ct.PhpNumber, ct.Double);
                gt_long_number = ct.PhpNumber.Operator(WellKnownMemberNames.GreaterThanOperatorName, ct.Long, ct.PhpNumber);
                lt_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.LessThanOperatorName, ct.PhpNumber, ct.PhpNumber);
                lt_number_long = ct.PhpNumber.Operator(WellKnownMemberNames.LessThanOperatorName, ct.PhpNumber, ct.Long);
                lt_number_double = ct.PhpNumber.Operator(WellKnownMemberNames.LessThanOperatorName, ct.PhpNumber, ct.Double);
                lt_long_number = ct.PhpNumber.Operator(WellKnownMemberNames.LessThanOperatorName, ct.Long, ct.PhpNumber);
            }

            public readonly CoreMethod
                ToLong, ToDouble, ToBoolean, ToString_Context, ToClass,
                CompareTo_number, CompareTo_long, CompareTo_double,
                Add_long_long, Add_long_double, Add_value_long, Add_value_double, Add_long_value, Add_double_value, Add_value_value, Add_value_array, Add_array_value,
                Subtract_long_long, Subtract_number_double, Subtract_long_double, Subtract_value_value, Subtract_value_long, Subtract_value_double, Subtract_value_number, Subtract_number_value, Subtract_long_value, Subtract_double_value,
                Negation_long,
                get_Long, get_Double,
                Mul_long_long, Mul_long_double, Mul_long_value, Mul_double_value, Mul_value_value, Mul_value_long, Mul_value_double,
                Pow_long_long, Pow_long_double, Pow_long_number, Pow_long_value, Pow_double_double, Pow_double_value, Pow_number_double, Pow_number_number, Pow_number_value, Pow_value_value,
                Mod_value_value, Mod_value_long, Mod_long_value, Mod_long_long,
                Create_Long, Create_Double;

            public readonly CoreOperator
                Eq_number_PhpValue, Ineq_number_PhpValue,
                Eq_number_number, Ineq_number_number,
                Eq_number_long, Ineq_number_long,
                Eq_number_double, Ineq_number_double,
                Eq_long_number, Ineq_long_number,
                Add_number_number, Add_number_long, Add_long_number, Add_value_number, Add_number_double, Add_double_number, Add_number_value,
                Subtract_number_number, Subtract_long_number, Subtract_number_long,
                Division_number_value, Division_number_number, Division_number_long, Division_number_double, Division_long_number,
                Mul_number_number, Mul_number_double, Mul_number_long, Mul_long_number, Mul_number_value, Mul_value_number,
                gt_number_number, gt_number_long, gt_number_double, gt_long_number,
                lt_number_number, lt_number_long, lt_number_double, lt_long_number,
                Negation;

            public readonly CoreField
                Default;
        }

        public struct IPhpConvertibleHolder
        {
            public IPhpConvertibleHolder(CoreTypes ct)
            {
                var t = ct.IPhpConvertible;

                ToBoolean = t.Method("ToBoolean");
                ToLong = t.Method("ToLong");
                ToDouble = t.Method("ToDouble");
                ToString_Context = t.Method("ToString", ct.Context);
                ToStringOrThrow_Context = t.Method("ToStringOrThrow", ct.Context);
                ToNumber = t.Method("ToNumber");
                ToClass = t.Method("ToClass");
            }

            public readonly CoreMethod
                ToLong, ToDouble, ToBoolean, ToString_Context, ToStringOrThrow_Context, ToNumber, ToClass;
        }

        public struct PhpStringHolder
        {
            public PhpStringHolder(CoreTypes ct)
            {
                ToBoolean = ct.PhpString.Method("ToBoolean");
                ToLong = ct.PhpString.Method("ToLong");
                ToDouble = ct.PhpString.Method("ToDouble");
                ToString_Context = ct.PhpString.Method("ToString", ct.Context);
                ToNumber = ct.PhpString.Method("ToNumber");
                ToBytes_Context = ct.PhpString.Method("ToBytes", ct.Context);

                EnsureWritable = ct.PhpString.Method("EnsureWritable");
                AsWritable_PhpString = ct.PhpString.Method("AsWritable", ct.PhpString);
                AsArray_PhpString = ct.PhpString.Method("AsArray", ct.PhpString);

                IsNull_PhpString = ct.PhpString.Method("IsNull", ct.PhpString);
            }

            public readonly CoreMethod
                ToLong, ToDouble, ToBoolean, ToString_Context, ToNumber, ToBytes_Context,
                EnsureWritable, AsWritable_PhpString, AsArray_PhpString,
                IsNull_PhpString;
        }

        public struct PhpStringBlobHolder
        {
            public PhpStringBlobHolder(CoreTypes ct)
            {
                Add_String = ct.PhpString_Blob.Method("Add", ct.String);
                Add_PhpString = ct.PhpString_Blob.Method("Add", ct.PhpString);
                Add_PhpValue_Context = ct.PhpString_Blob.Method("Add", ct.PhpValue, ct.Context);
            }

            public readonly CoreMethod
                Add_String, Add_PhpString, Add_PhpValue_Context;
        }

        public struct IPhpArrayHolder
        {
            public IPhpArrayHolder(CoreTypes ct)
            {
                var arr = ct.IPhpArray;

                RemoveKey_IntStringKey = arr.Method("RemoveKey", ct.IntStringKey);
                RemoveKey_PhpValue = arr.Method("RemoveKey", ct.PhpValue);

                GetItemValue_IntStringKey = arr.Method("GetItemValue", ct.IntStringKey);
                GetItemValue_PhpValue = arr.Method("GetItemValue", ct.PhpValue);

                SetItemValue_IntStringKey_PhpValue = arr.Method("SetItemValue", ct.IntStringKey, ct.PhpValue);
                SetItemValue_PhpValue_PhpValue = arr.Method("SetItemValue", ct.PhpValue, ct.PhpValue);

                SetItemAlias_IntStringKey_PhpAlias = arr.Method("SetItemAlias", ct.IntStringKey, ct.PhpAlias);
                SetItemAlias_PhpValue_PhpAlias = arr.Method("SetItemAlias", ct.PhpValue, ct.PhpAlias);

                AddValue_PhpValue = arr.Method("AddValue", ct.PhpValue);

                EnsureItemObject_IntStringKey = arr.Method("EnsureItemObject", ct.IntStringKey);
                EnsureItemArray_IntStringKey = arr.Method("EnsureItemArray", ct.IntStringKey);
                EnsureItemAlias_IntStringKey = arr.Method("EnsureItemAlias", ct.IntStringKey);

                Count = arr.Property("Count");
            }

            public readonly CoreProperty
                Count;

            public readonly CoreMethod
                RemoveKey_IntStringKey, RemoveKey_PhpValue,
                GetItemValue_IntStringKey, GetItemValue_PhpValue,
                SetItemValue_IntStringKey_PhpValue, SetItemValue_PhpValue_PhpValue,
                SetItemAlias_IntStringKey_PhpAlias, SetItemAlias_PhpValue_PhpAlias,
                AddValue_PhpValue,
                EnsureItemObject_IntStringKey, EnsureItemArray_IntStringKey, EnsureItemAlias_IntStringKey;
        }

        public struct PhpArrayHolder
        {
            public PhpArrayHolder(CoreTypes ct)
            {
                var t = ct.PhpArray;

                //
                ToString_Context = t.Method("ToString", ct.Context);
                ToClass = t.Method("ToClass");
                ToBoolean = t.Method("ToBoolean");

                RemoveKey_IntStringKey = t.Method("RemoveKey", ct.IntStringKey);

                GetItemValue_IntStringKey = t.Method("GetItemValue", ct.IntStringKey);
                GetItemRef_IntStringKey = t.Method("GetItemRef", ct.IntStringKey);

                DeepCopy = t.Method("DeepCopy");
                GetForeachEnumerator_Boolean = t.Method("GetForeachEnumerator", ct.Boolean);

                SetItemValue_IntStringKey_PhpValue = t.Method("SetItemValue", ct.IntStringKey, ct.PhpValue);
                SetItemAlias_IntStringKey_PhpAlias = t.Method("SetItemAlias", ct.IntStringKey, ct.PhpAlias);

                EnsureItemObject_IntStringKey = t.Method("EnsureItemObject", ct.IntStringKey);
                EnsureItemArray_IntStringKey = t.Method("EnsureItemArray", ct.IntStringKey);
                EnsureItemAlias_IntStringKey = t.Method("EnsureItemAlias", ct.IntStringKey);

                Add_PhpValue = ct.PhpHashtable.Method("Add", ct.PhpValue);
                Add_IntStringKey_PhpValue = ct.PhpHashtable.Method("Add", ct.IntStringKey, ct.PhpValue);

                New_PhpValue = t.Method("New", ct.PhpValue);
                Union_PhpArray_PhpArray = t.Method("Union", ct.PhpArray, ct.PhpArray);

                Empty = t.Field("Empty");
            }

            public readonly CoreMethod
                ToClass, ToString_Context, ToBoolean,
                RemoveKey_IntStringKey,
                GetItemValue_IntStringKey, GetItemRef_IntStringKey,
                SetItemValue_IntStringKey_PhpValue, SetItemAlias_IntStringKey_PhpAlias, Add_PhpValue,
                EnsureItemObject_IntStringKey, EnsureItemArray_IntStringKey, EnsureItemAlias_IntStringKey,
                DeepCopy, GetForeachEnumerator_Boolean,
                Add_IntStringKey_PhpValue,
                New_PhpValue, Union_PhpArray_PhpArray;

            public readonly CoreField
                Empty;
        }

        public struct ConstructorsHolder
        {
            public ConstructorsHolder(CoreTypes ct)
            {
                PhpAlias_PhpValue_int = ct.PhpAlias.Ctor(ct.PhpValue, ct.Int32);
                PhpString_Blob = ct.PhpString.Ctor(ct.PhpString_Blob);
                PhpString_string = ct.PhpString.Ctor(ct.String);
                PhpString_string_string = ct.PhpString.Ctor(ct.String, ct.String);
                PhpString_PhpValue_Context = ct.PhpString.Ctor(ct.PhpValue, ct.Context);
                PhpString_PhpString = ct.PhpString.Ctor(ct.PhpString);
                Blob = ct.PhpString_Blob.Ctor();
                PhpArray = ct.PhpArray.Ctor();
                PhpArray_int = ct.PhpArray.Ctor(ct.Int32);
                IntStringKey_int = ct.IntStringKey.Ctor(ct.Int32);
                IntStringKey_string = ct.IntStringKey.Ctor(ct.String);
                ScriptAttribute_string = ct.ScriptAttribute.Ctor(ct.String);
                PhpTraitAttribute = ct.PhpTraitAttribute.Ctor();
                PhpTypeAttribute_string_string = ct.PhpTypeAttribute.Ctor(ct.String, ct.String);
                PhpFieldsOnlyCtorAttribute = ct.PhpFieldsOnlyCtorAttribute.Ctor();
                NotNullAttribute = ct.NotNullAttribute.Ctor();

                ScriptDiedException = ct.ScriptDiedException.Ctor();
                ScriptDiedException_Long = ct.ScriptDiedException.Ctor(ct.Long);
                ScriptDiedException_PhpValue = ct.ScriptDiedException.Ctor(ct.PhpValue);
            }

            public readonly CoreConstructor
                PhpAlias_PhpValue_int,
                PhpArray, PhpArray_int,
                PhpString_Blob, PhpString_string, PhpString_PhpString, PhpString_string_string, PhpString_PhpValue_Context,
                Blob,
                IntStringKey_int, IntStringKey_string,
                ScriptAttribute_string, PhpTraitAttribute, PhpTypeAttribute_string_string, PhpFieldsOnlyCtorAttribute, NotNullAttribute,
                ScriptDiedException, ScriptDiedException_Long, ScriptDiedException_PhpValue;
        }

        public struct ContextHolder
        {
            public ContextHolder(CoreTypes ct)
            {
                AddScriptReference_TScript = ct.Context.Method("AddScriptReference");
                Dispose = ct.Context.Method("Dispose");

                DeclareFunction_RoutineInfo = ct.Context.Method("DeclareFunction", ct.RoutineInfo);
                DeclareType_T = ct.Context.Method("DeclareType");
                DeclareType_PhpTypeInfo_String = ct.Context.Method("DeclareType", ct.PhpTypeInfo, ct.String);

                DisableErrorReporting = ct.Context.Method("DisableErrorReporting");
                EnableErrorReporting = ct.Context.Method("EnableErrorReporting");

                CheckIncludeOnce_TScript = ct.Context.Method("CheckIncludeOnce");
                OnInclude_TScript = ct.Context.Method("OnInclude");
                Include_string_string_PhpArray_object_RuntimeTypeHandle_bool_bool = ct.Context.Method("Include", ct.String, ct.String, ct.PhpArray, ct.Object, ct.RuntimeTypeHandle, ct.Boolean, ct.Boolean);

                ExpectTypeDeclared_T = ct.Context.Method("ExpectTypeDeclared");

                GetStatic_T = ct.Context.Method("GetStatic");
                GetDeclaredType_string_bool = ct.Context.Method("GetDeclaredType", ct.String, ct.Boolean);
                GetDeclaredTypeOrThrow_string_bool = ct.Context.Method("GetDeclaredTypeOrThrow", ct.String, ct.Boolean);

                Assert_bool_PhpValue = ct.Context.Method("Assert", ct.Boolean, ct.PhpValue);

                // properties
                RootPath = ct.Context.Property("RootPath");
                Globals = ct.Context.Property("Globals");
                Server = ct.Context.Property("Server");
                Request = ct.Context.Property("Request");
                Get = ct.Context.Property("Get");
                Post = ct.Context.Property("Post");
                Cookie = ct.Context.Property("Cookie");
                Env = ct.Context.Property("Env");
                Files = ct.Context.Property("Files");
                Session = ct.Context.Property("Session");
                HttpRawPostData = ct.Context.Property("HttpRawPostData");
            }

            public readonly CoreMethod
                AddScriptReference_TScript,
                DeclareFunction_RoutineInfo, DeclareType_T, DeclareType_PhpTypeInfo_String,
                DisableErrorReporting, EnableErrorReporting,
                CheckIncludeOnce_TScript, OnInclude_TScript, Include_string_string_PhpArray_object_RuntimeTypeHandle_bool_bool,
                ExpectTypeDeclared_T,
                GetStatic_T,
                GetDeclaredType_string_bool, GetDeclaredTypeOrThrow_string_bool,
                Assert_bool_PhpValue,
                Dispose;

            public readonly CoreProperty
                RootPath,
                Globals, Server, Request, Get, Post, Cookie, Env, Files, Session, HttpRawPostData;
        }

        public struct DynamicHolder
        {
            public DynamicHolder(CoreTypes ct)
            {
                BinderFactory_Function = ct.BinderFactory.Method("Function", ct.String, ct.String, ct.RuntimeTypeHandle, ct.Int32);
                BinderFactory_InstanceFunction = ct.BinderFactory.Method("InstanceFunction", ct.String, ct.RuntimeTypeHandle, ct.RuntimeTypeHandle, ct.Int32);
                BinderFactory_StaticFunction = ct.BinderFactory.Method("StaticFunction", ct.RuntimeTypeHandle, ct.String, ct.RuntimeTypeHandle, ct.RuntimeTypeHandle, ct.Int32);

                GetFieldBinder = ct.BinderFactory.Method("GetField", ct.String, ct.RuntimeTypeHandle, ct.RuntimeTypeHandle, ct.AccessMask);
                SetFieldBinder = ct.BinderFactory.Method("SetField", ct.String, ct.RuntimeTypeHandle, ct.AccessMask);
                GetClassConstBinder = ct.BinderFactory.Method("GetClassConst", ct.String, ct.RuntimeTypeHandle, ct.RuntimeTypeHandle, ct.AccessMask);

                GetPhpTypeInfo_T = ct.PhpTypeInfoExtension.Method("GetPhpTypeInfo");
                GetPhpTypeInfo_Object = ct.PhpTypeInfoExtension.Method("GetPhpTypeInfo", ct.Object);
                GetPhpTypeInfo_RuntimeTypeHandle = ct.PhpTypeInfoExtension.Method("GetPhpTypeInfo", ct.RuntimeTypeHandle);
            }

            public readonly CoreMethod
                BinderFactory_Function, BinderFactory_InstanceFunction, BinderFactory_StaticFunction,
                GetFieldBinder, SetFieldBinder, GetClassConstBinder,
                GetPhpTypeInfo_T, GetPhpTypeInfo_Object, GetPhpTypeInfo_RuntimeTypeHandle;
        }

        public struct ReflectionHolder
        {
            readonly CoreTypes _ct;

            public ReflectionHolder(CoreTypes ct)
            {
                _ct = ct;
                _lazyCreateUserRoutine = null;
            }

            public MethodSymbol CreateUserRoutine_string_RuntimeMethodHandle_RuntimeMethodHandleArr
            {
                get
                {
                    if (_lazyCreateUserRoutine == null)
                    {
                        _lazyCreateUserRoutine = _ct.RoutineInfo.Symbol.GetMembers("CreateUserRoutine").OfType<MethodSymbol>().Single(m =>
                            m.ParameterCount == 3 &&
                            m.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                            m.Parameters[1].Type.Name == "RuntimeMethodHandle" &&
                            m.Parameters[2].Type.IsSZArray());
                    }

                    return _lazyCreateUserRoutine;
                }
            }
            MethodSymbol _lazyCreateUserRoutine;
        }

    }
}
