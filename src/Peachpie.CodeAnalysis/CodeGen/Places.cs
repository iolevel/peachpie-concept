﻿using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using Devsense.PHP.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeGen;
using Pchp.CodeAnalysis.FlowAnalysis;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Symbols;
using Peachpie.CodeAnalysis.Utilities;
using Cci = Microsoft.Cci;

namespace Pchp.CodeAnalysis.CodeGen
{
    #region IPlace

    /// <summary>
    /// Interface supported by storage places with address.
    /// </summary>
    internal interface IPlace
    {
        /// <summary>
        /// Gets the type of place.
        /// </summary>
        TypeSymbol TypeOpt { get; }

        /// <summary>
        /// Emits code that loads the value from this storage place.
        /// </summary>
        /// <param name="il">The <see cref="ILBuilder"/> to emit the code to.</param>
        TypeSymbol EmitLoad(ILBuilder il);

        /// <summary>
        /// Emits preparation code for storing a value into the place.
        /// Must be call before loading a value and calling <see cref="EmitStore(ILBuilder)"/>.
        /// </summary>
        /// <param name="il">The <see cref="ILBuilder"/> to emit the code to.</param>
        void EmitStorePrepare(ILBuilder il);

        /// <summary>
        /// Emits code that stores a value to this storage place.
        /// </summary>
        /// <param name="il">The <see cref="ILBuilder"/> to emit the code to.</param>
        void EmitStore(ILBuilder il);

        /// <summary>
        /// Emits code that loads address of this storage place.
        /// </summary>
        /// <param name="il">The <see cref="ILBuilder"/> to emit the code to.</param>
        void EmitLoadAddress(ILBuilder il);

        /// <summary>
        /// Gets whether the place has an address.
        /// </summary>
        bool HasAddress { get; }
    }

    #endregion

    #region Places

    internal class LocalPlace : IPlace
    {
        readonly LocalDefinition _def;

        public override string ToString() => $"${_def.Name}";

        public LocalPlace(LocalDefinition def)
        {
            Contract.ThrowIfNull(def);
            _def = def;
        }

        public TypeSymbol TypeOpt => (TypeSymbol)_def.Type;

        public bool HasAddress => true;

        public TypeSymbol EmitLoad(ILBuilder il)
        {
            il.EmitLocalLoad(_def);
            return (TypeSymbol)_def.Type;
        }

        public void EmitLoadAddress(ILBuilder il) => il.EmitLocalAddress(_def);

        public void EmitStorePrepare(ILBuilder il) { }

        public void EmitStore(ILBuilder il) => il.EmitLocalStore(_def);
    }

    internal class ParamPlace : IPlace
    {
        readonly ParameterSymbol _p;

        public int Index => ((MethodSymbol)_p.ContainingSymbol).HasThis ? _p.Ordinal + 1 : _p.Ordinal;

        public override string ToString() => $"${_p.Name}";

        public ParamPlace(ParameterSymbol p)
        {
            Contract.ThrowIfNull(p);
            Debug.Assert(p.Ordinal >= 0, "(p.Ordinal < 0)");
            Debug.Assert(p is SourceParameterSymbol sp ? !sp.IsFake : true);
            _p = p;
        }

        public TypeSymbol TypeOpt => _p.Type;

        public bool HasAddress => true;

        public TypeSymbol EmitLoad(ILBuilder il)
        {
            il.EmitLoadArgumentOpcode(Index);
            return _p.Type;
        }

        public void EmitLoadAddress(ILBuilder il) => il.EmitLoadArgumentAddrOpcode(Index);

        public void EmitStorePrepare(ILBuilder il) { }

        public void EmitStore(ILBuilder il) => il.EmitStoreArgumentOpcode(Index);
    }

    internal class ArgPlace : IPlace
    {
        readonly int _index;
        readonly TypeSymbol _type;

        public int Index => _index;

        public override string ToString() => $"${_index}";

        public ArgPlace(TypeSymbol t, int index)
        {
            Contract.ThrowIfNull(t);
            _type = t;
            _index = index;
        }

        public TypeSymbol TypeOpt => _type;

        public bool HasAddress => true;

        public TypeSymbol EmitLoad(ILBuilder il)
        {
            il.EmitLoadArgumentOpcode(Index);
            return _type;
        }

        public void EmitLoadAddress(ILBuilder il) => il.EmitLoadArgumentAddrOpcode(Index);

        public void EmitStorePrepare(ILBuilder il) { }

        public void EmitStore(ILBuilder il) => il.EmitStoreArgumentOpcode(Index);
    }

    /// <summary>
    /// Place wrapper allowing only read operation.
    /// </summary>
    internal class ReadOnlyPlace : IPlace
    {
        readonly IPlace _place;

        public ReadOnlyPlace(IPlace place)
        {
            Contract.ThrowIfNull(place);
            _place = place;
        }

        public bool HasAddress => _place.HasAddress;

        public TypeSymbol TypeOpt => _place.TypeOpt;

        public TypeSymbol EmitLoad(ILBuilder il) => _place.EmitLoad(il);

        public void EmitLoadAddress(ILBuilder il) => _place.EmitLoadAddress(il);

        public void EmitStore(ILBuilder il)
        {
            throw new InvalidOperationException($"{_place} is readonly!");  // TODO: ErrCode
        }

        public void EmitStorePrepare(ILBuilder il) { }
    }

    internal class FieldPlace : IPlace
    {
        readonly IPlace _holder;
        readonly FieldSymbol _field;
        readonly Cci.IFieldReference _fieldref;

        internal static IFieldSymbol GetRealDefinition(IFieldSymbol field)
        {
            // field redeclares its parent member, use the original def
            return (field is SourceFieldSymbol srcf)
                ? srcf.OverridenDefinition ?? field
                : field;
        }

        public FieldPlace(IPlace holder, IFieldSymbol field, Emit.PEModuleBuilder module = null)
        {
            Contract.ThrowIfNull(field);

            field = GetRealDefinition(field);

            _holder = holder;
            _field = (FieldSymbol)field;
            _fieldref = (module != null) ? module.Translate((FieldSymbol)field, null, DiagnosticBag.GetInstance()) : (Cci.IFieldReference)field;

            Debug.Assert(holder != null || field.IsStatic);
            Debug.Assert(holder == null || holder.TypeOpt.IsOfType(_field.ContainingType) || _field.ContainingType.IsValueType);
        }

        void EmitHolder(ILBuilder il)
        {
            Debug.Assert(_field.IsStatic == (_holder == null));

            if (_holder != null)
            {
                if (_holder.TypeOpt != null && _holder.TypeOpt.IsValueType)
                {
                    Debug.Assert(_holder.HasAddress);
                    _holder.EmitLoadAddress(il);
                }
                else
                {
                    _holder.EmitLoad(il);
                }
            }
        }

        void EmitOpCode(ILBuilder il, ILOpCode code)
        {
            il.EmitOpCode(code);
            il.EmitToken(_fieldref, null, DiagnosticBag.GetInstance());    // .{field}
        }

        public TypeSymbol TypeOpt => _field.Type;

        public bool HasAddress => true;

        public TypeSymbol EmitLoad(ILBuilder il)
        {
            EmitHolder(il);
            EmitOpCode(il, _field.IsStatic ? ILOpCode.Ldsfld : ILOpCode.Ldfld);
            return _field.Type;
        }

        public void EmitStorePrepare(ILBuilder il)
        {
            EmitHolder(il);
        }

        public void EmitStore(ILBuilder il)
        {
            EmitOpCode(il, _field.IsStatic ? ILOpCode.Stsfld : ILOpCode.Stfld);
        }

        public void EmitLoadAddress(ILBuilder il)
        {
            EmitHolder(il);
            EmitOpCode(il, _field.IsStatic ? ILOpCode.Ldsflda : ILOpCode.Ldflda);
        }
    }

    internal class PropertyPlace : IPlace
    {
        readonly IPlace _holder;
        readonly PropertySymbol _property;

        public PropertyPlace(IPlace holder, Cci.IPropertyDefinition property)
        {
            Contract.ThrowIfNull(property);

            _holder = holder;
            _property = (PropertySymbol)property;
        }

        public TypeSymbol TypeOpt => _property.Type;

        public bool HasAddress => false;

        public TypeSymbol EmitLoad(ILBuilder il)
        {
            //if (_property.Getter == null)
            //    throw new InvalidOperationException();

            var stack = +1;
            var getter = _property.GetMethod;

            if (_holder != null)
            {
                Debug.Assert(!getter.IsStatic);
                _holder.EmitLoad(il);   // {holder}
                stack -= 1;
            }

            il.EmitOpCode(getter.IsVirtual ? ILOpCode.Callvirt : ILOpCode.Call, stack);
            il.EmitToken(getter, null, DiagnosticBag.GetInstance());

            //
            return getter.ReturnType;
        }

        public void EmitLoadAddress(ILBuilder il)
        {
            throw new NotSupportedException();
        }

        public void EmitStorePrepare(ILBuilder il)
        {
            if (_holder != null)
            {
                Debug.Assert(_property.SetMethod != null);
                Debug.Assert(!_property.SetMethod.IsStatic);
                _holder.EmitLoad(il);   // {holder}
            }
        }

        public void EmitStore(ILBuilder il)
        {
            //if (_property.Setter == null)
            //    throw new InvalidOperationException();

            var stack = 0;
            var setter = _property.SetMethod;

            if (_holder != null)
            {
                stack -= 1;
            }

            //
            il.EmitOpCode(setter.IsVirtual ? ILOpCode.Callvirt : ILOpCode.Call, stack);
            il.EmitToken(setter, null, DiagnosticBag.GetInstance());

            //
            Debug.Assert(setter.ReturnType.SpecialType == SpecialType.System_Void);
        }
    }

    internal class OperatorPlace : IPlace
    {
        readonly MethodSymbol _operator;
        readonly IPlace _operand;

        public OperatorPlace(MethodSymbol @operator, IPlace operand)
        {
            Debug.Assert(@operator != null);
            Debug.Assert(@operator.HasThis == false);
            Debug.Assert(operand != null);

            _operator = @operator;
            _operand = operand;
        }

        public TypeSymbol TypeOpt => _operator.ReturnType;

        public bool HasAddress => false;

        public TypeSymbol EmitLoad(ILBuilder il)
        {
            _operand.EmitLoad(il);

            il.EmitOpCode(ILOpCode.Call, _operator.GetCallStackBehavior());
            il.EmitToken(_operator, null, DiagnosticBag.GetInstance());

            return TypeOpt;
        }

        public void EmitLoadAddress(ILBuilder il)
        {
            throw new NotSupportedException();
        }

        public void EmitStore(ILBuilder il)
        {
            throw new NotSupportedException();
        }

        public void EmitStorePrepare(ILBuilder il)
        {
            throw new NotSupportedException();
        }
    }

    #endregion

    #region IBoundReference

    /// <summary>
    /// Helper object emitting value of a member instance.
    /// Used to avoid repetitious evaluation of the instance in case of BoundCompoundAssignEx or Increment/Decrement.
    /// </summary>
    internal sealed class InstanceCacheHolder : IDisposable
    {
        LocalDefinition _instance_loc;
        LocalDefinition _name_loc;
        CodeGenerator _cg;

        public void Dispose()
        {
            if (_instance_loc != null)
            {
                _cg.ReturnTemporaryLocal(_instance_loc);
                _instance_loc = null;
            }

            if (_name_loc != null)
            {
                _cg.ReturnTemporaryLocal(_name_loc);
                _name_loc = null;
            }

            _cg = null;
        }

        /// <summary>
        /// Emits instance. Caches the result if holder is provided, or loads evaluated instance if holder was initialized already.
        /// </summary>
        public static TypeSymbol EmitInstance(InstanceCacheHolder holderOrNull, CodeGenerator cg, BoundExpression instance)
        {
            return (instance != null) ? EmitInstance(holderOrNull, cg, () => cg.Emit(instance)) : null;
        }

        public static TypeSymbol EmitInstance(InstanceCacheHolder holderOrNull, CodeGenerator cg, Func<TypeSymbol> emitter)
        {
            if (holderOrNull != null)
            {
                return holderOrNull.EmitInstance(cg, emitter);
            }
            else
            {
                return emitter();
            }
        }

        /// <summary>
        /// Emits name as string. Caches the result if holder is provided, or loads evaluated name if holder was initialized already.
        /// </summary>
        public static void EmitName(InstanceCacheHolder holderOrNull, CodeGenerator cg, BoundExpression name)
        {
            Contract.ThrowIfNull(cg);
            Contract.ThrowIfNull(name);

            if (holderOrNull != null)
            {
                holderOrNull.EmitName(cg, name);
            }
            else
            {
                cg.EmitConvert(name, cg.CoreTypes.String);
            }
        }

        /// <summary>
        /// Emits <see name="_instance"/>, uses cached value if initialized already.
        /// </summary>
        TypeSymbol EmitInstance(CodeGenerator cg, Func<TypeSymbol> emitter)
        {
            Debug.Assert(cg != null);

            if (_instance_loc != null)
            {
                cg.Builder.EmitLocalLoad(_instance_loc);
            }
            else
            {
                _cg = cg;

                // return (<loc> = <instance>);
                _instance_loc = cg.GetTemporaryLocal(emitter());
                cg.EmitOpCode(ILOpCode.Dup);
                cg.Builder.EmitLocalStore(_instance_loc);
            }

            return (TypeSymbol)_instance_loc.Type;
        }

        /// <summary>
        /// Emits name as string, uses cached variable.
        /// </summary>
        void EmitName(CodeGenerator cg, BoundExpression name)
        {
            Contract.ThrowIfNull(cg);
            Contract.ThrowIfNull(name);

            if (_name_loc != null)
            {
                cg.Builder.EmitLocalLoad(_name_loc);
            }
            else
            {
                _cg = cg;

                // return (<loc> = <name>)
                _name_loc = cg.GetTemporaryLocal(cg.CoreTypes.String);
                cg.EmitConvert(name, cg.CoreTypes.String);
                cg.Builder.EmitOpCode(ILOpCode.Dup);
                cg.Builder.EmitLocalStore(_name_loc);
            }
        }
    }

    /// <summary>
    /// Interface wrapping bound storage places.
    /// </summary>
    /// <remarks>
    /// Provides methods for load and store to the place.
    /// 1. EmitLoadPrepare, EmitStorePrepare. Emits first argument of load/store operation. Can be stored to a temporary variable to both load and store.
    /// 2. EmitLoad. Loads value to the top of stack. Expect 1. to be on stack first.
    /// 3. EmitStore. Stores value from top of stack to the place. Expects 1. to be on stack before.
    /// </remarks>
    internal interface IBoundReference
    {
        /// <summary>
        /// Type of place. Can be <c>null</c>.
        /// </summary>
        TypeSymbol TypeOpt { get; }

        /// <summary>
        /// Emits the preamble to the load operation.
        /// Must be called before <see cref="EmitLoad(CodeGenerator)"/>.
        /// </summary>
        /// <param name="cg">Code generator, must not be <c>null</c>.</param>
        /// <param name="instanceOpt">Temporary variable holding value of instance expression.</param>
        /// <remarks>
        /// The returned type corresponds to the instance expression emitted onto top of the evaluation stack. The value can be stored to a temporary variable and passed to the next call to Emit**Prepare to avoid evaluating of instance again.
        /// </remarks>
        void EmitLoadPrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt = null);

        /// <summary>
        /// Emits the preamble to the load operation.
        /// Must be called before <see cref="EmitStore(CodeGenerator, TypeSymbol)"/>.
        /// </summary>
        /// <param name="cg">Code generator, must not be <c>null</c>.</param>
        /// <param name="instanceOpt">Temporary variable holding value of instance expression.</param>
        /// <remarks>
        /// The returned type corresponds to the instance expression emitted onto top of the evaluation stack. The value can be stored to a temporary variable and passed to the next call to Emit**Prepare to avoid evaluating of instance again.
        /// </remarks>
        void EmitStorePrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt = null);

        /// <summary>
        /// Emits load of value.
        /// Expects <see cref="EmitLoadPrepare"/> to be called first.
        /// </summary>
        TypeSymbol EmitLoad(CodeGenerator cg);

        /// <summary>
        /// Emits code to stores a value to this place.
        /// Expects <see cref="EmitStorePrepare(CodeGenerator,InstanceCacheHolder)"/> and actual value to be loaded on stack.
        /// </summary>
        /// <param name="cg">Code generator.</param>
        /// <param name="valueType">Type of value on the stack to be stored. Can be <c>null</c> in case of <c>Unset</c> semantic.</param>
        void EmitStore(CodeGenerator cg, TypeSymbol valueType);

        ///// <summary>
        ///// Emits code that loads address of this storage place.
        ///// Expects <see cref="EmitPrepare(CodeGenerator)"/> to be called first. 
        ///// </summary>
        //void EmitLoadAddress(CodeGenerator cg);

        ///// <summary>
        ///// Gets whether the place has an address.
        ///// </summary>
        //bool HasAddress { get; }
    }

    #endregion

    #region BoundLocalPlace

    internal class BoundLocalPlace : IBoundReference, IPlace
    {
        readonly IPlace _place;
        readonly BoundAccess _access;
        readonly TypeRefMask _thint;

        public BoundLocalPlace(IPlace place, BoundAccess access, TypeRefMask thint)
        {
            Contract.ThrowIfNull(place);

            _place = place;
            _access = access;
            _thint = thint;
        }

        public void EmitLoadPrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt) { }

        public TypeSymbol EmitLoad(CodeGenerator cg)
        {
            Debug.Assert(_access.IsRead);

            var type = _place.TypeOpt;

            // Ensure Object ($x->.. =)
            if (_access.EnsureObject)
            {
                if (type == cg.CoreTypes.PhpAlias)
                {
                    _place.EmitLoad(cg.Builder);
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpAlias.EnsureObject)
                        .Expect(SpecialType.System_Object);
                }
                else if (type == cg.CoreTypes.PhpValue)
                {
                    if (!_place.HasAddress) throw cg.NotImplementedException("unreachable: variable does not have an address");

                    _place.EmitLoadAddress(cg.Builder);
                    cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.EnsureObject)
                        .Expect(SpecialType.System_Object);

                    if (_thint.IsSingleType && cg.IsClassOnly(_thint))
                    {
                        var tref = cg.Routine.TypeRefContext.GetTypes(_thint)[0];
                        var clrtype = (TypeSymbol)cg.DeclaringCompilation.GetTypeByMetadataName(tref.QualifiedName.ClrName());
                        if (clrtype != null && !clrtype.IsErrorType() && clrtype != cg.CoreTypes.Object)
                        {
                            cg.EmitCastClass(clrtype);
                            return clrtype;
                        }
                    }

                    return cg.CoreTypes.Object;
                }
                else if (type.IsOfType(cg.CoreTypes.IPhpArray))
                {
                    // PhpArray -> stdClass
                    // PhpString -> stdClass (?)
                    // otherwise keep the instance on stack
                    throw new NotImplementedException();
                }
                else
                {
                    if (type.IsReferenceType)
                    {
                        if (type == cg.CoreTypes.Object && cg.TypeRefContext.IsNull(_thint) && _place.HasAddress)
                        {
                            // Operators.EnsureObject(ref <place>)
                            _place.EmitLoadAddress(cg.Builder);
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.EnsureObject_ObjectRef)
                                .Expect(SpecialType.System_Object);
                        }
                        else
                        {
                            // <place>
                            return _place.EmitLoad(cg.Builder);
                        }
                    }
                    else
                    {
                        // return new stdClass(ctx)
                        throw new NotImplementedException();
                    }
                }
            }
            // Ensure Array ($x[] =)
            else if (_access.EnsureArray)
            {
                if (type == cg.CoreTypes.PhpAlias)
                {
                    // <place>.EnsureArray()
                    _place.EmitLoad(cg.Builder);
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpAlias.EnsureArray)
                        .Expect(cg.CoreTypes.IPhpArray);
                }
                else if (type == cg.CoreTypes.PhpValue)
                {
                    if (cg.IsArrayOnly(_thint))
                    {
                        // uses typehint and accesses .Array directly if possible
                        // <place>.Array
                        _place.EmitLoadAddress(cg.Builder);
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.get_Array)
                            .Expect(cg.CoreTypes.PhpArray);
                    }
                    else
                    {
                        // <place>.EnsureArray()
                        _place.EmitLoadAddress(cg.Builder);
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.EnsureArray)
                            .Expect(cg.CoreTypes.IPhpArray);
                    }
                }
                else if (type == cg.CoreTypes.PhpString)
                {
                    Debug.Assert(type.IsValueType);
                    // <place>.EnsureWritable() : IPhpArray
                    _place.EmitLoadAddress(cg.Builder); // LOAD ref PhpString
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpString.EnsureWritable);
                }
                else if (type.IsOfType(cg.CoreTypes.IPhpArray))
                {
                    // Operators.EnsureArray(ref <place>)
                    _place.EmitLoadAddress(cg.Builder);

                    if (type == cg.CoreTypes.PhpArray)
                    {
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.EnsureArray_PhpArrayRef)
                            .Expect(cg.CoreTypes.PhpArray);
                    }
                    else
                    {
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.EnsureArray_IPhpArrayRef)
                            .Expect(cg.CoreTypes.IPhpArray);
                    }
                }
                else if (type.IsOfType(cg.CoreTypes.ArrayAccess))
                {
                    // LOAD <place> : ArrayAccess
                    return _place.EmitLoad(cg.Builder);
                }

                throw cg.NotImplementedException("EnsureArray(" + type.Name + ")");
            }
            // Ensure Alias (&$x)
            else if (_access.IsReadRef)
            {
                if (type == cg.CoreTypes.PhpAlias)
                {
                    // TODO: <place>.AddRef()
                    return _place.EmitLoad(cg.Builder);
                }
                else if (type == cg.CoreTypes.PhpValue)
                {
                    // return <place>.EnsureAlias()
                    _place.EmitLoadAddress(cg.Builder);
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.EnsureAlias)
                        .Expect(cg.CoreTypes.PhpAlias);
                }
                else if (type == cg.CoreTypes.PhpNumber)
                {
                    throw new NotImplementedException();
                }
                else
                {
                    // new PhpAlias((PhpValue)<place>, 1)   // e.g. &$this
                    cg.EmitConvertToPhpValue(_place.EmitLoad(cg.Builder), 0);
                    return cg.Emit_PhpValue_MakeAlias();
                }
            }
            else
            {
                // Read Copy
                if (_access.IsReadValueCopy)
                {
                    if (type == cg.CoreTypes.PhpValue && _place.HasAddress)
                    {
                        _place.EmitLoadAddress(cg.Builder);

                        if (_thint.IsRef)
                        {
                            // Template: <place>.GetValue().DeepCopy()
                            return cg.EmitDeepCopy(cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.GetValue));
                        }
                        else
                        {
                            // Template: <place>.DeepCopy()
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.DeepCopy);
                        }
                    }

                    // 
                    var t = _place.EmitLoad(cg.Builder);
                    if (_thint.IsRef)
                    {
                        t = cg.EmitDereference(t);
                    }

                    //
                    return cg.EmitDeepCopy(t);
                }
                else if (_access.IsReadValue && _thint.IsRef)
                {
                    // Read Value
                    if (type == cg.CoreTypes.PhpValue && _place.HasAddress)
                    {
                        // Template: <ref place>.GetValue()
                        _place.EmitLoadAddress(cg.Builder);
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.GetValue);
                    }

                    return cg.EmitDereference(_place.EmitLoad(cg.Builder));
                }
                else
                {
                    // Read
                    return _place.EmitLoad(cg.Builder);
                }
            }
        }

        public void EmitStorePrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt)
        {
            var type = _place.TypeOpt;

            if (_access.IsWriteRef || _access.IsUnset)
            {
                // no need for preparation
                _place.EmitStorePrepare(cg.Builder);
            }
            else
            {
                //
                if (type == cg.CoreTypes.PhpAlias)
                {
                    // (PhpAlias)<place>
                    _place.EmitLoad(cg.Builder);
                }
                else if (type == cg.CoreTypes.PhpValue)
                {
                    if (_thint.IsRef)
                    {
                        // Operators.SetValue(ref <place>, (PhpValue)<value>);
                        _place.EmitLoadAddress(cg.Builder);
                    }
                    else
                    {
                        _place.EmitStorePrepare(cg.Builder);
                    }
                }
                else
                {
                    // no need for preparation
                    _place.EmitStorePrepare(cg.Builder);
                }
            }
        }

        public void EmitStore(CodeGenerator cg, TypeSymbol valueType)
        {
            var type = _place.TypeOpt;

            // Write Ref
            if (_access.IsWriteRef)
            {
                if (valueType != cg.CoreTypes.PhpAlias)
                {
                    Debug.Assert(false, "caller should get aliased value");
                    cg.EmitConvertToPhpValue(valueType, 0);
                    valueType = cg.Emit_PhpValue_MakeAlias();
                }

                //
                if (type == cg.CoreTypes.PhpAlias)
                {
                    // <place> = <alias>
                    _place.EmitStore(cg.Builder);
                }
                else if (type == cg.CoreTypes.PhpValue)
                {
                    // <place> = PhpValue.Create(<alias>)
                    cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.Create_PhpAlias);
                    _place.EmitStore(cg.Builder);
                }
                else
                {
                    Debug.Assert(false, "Assigning alias to non-aliasable variable.");
                    cg.EmitConvert(valueType, 0, type);
                    _place.EmitStore(cg.Builder);
                }
            }
            else if (_access.IsUnset)
            {
                Debug.Assert(valueType == null);

                // <place> =

                if (type == cg.CoreTypes.PhpAlias)
                {
                    // new PhpAlias(void)
                    cg.Emit_PhpValue_Void();
                    cg.Emit_PhpValue_MakeAlias();
                }
                else if (type.IsReferenceType)
                {
                    // null
                    cg.Builder.EmitNullConstant();
                }
                else
                {
                    // TODO: unset of static local makes it regular local variable

                    // default(T)
                    cg.EmitLoadDefaultOfValueType(type);
                }

                _place.EmitStore(cg.Builder);
            }
            else
            {
                //
                if (type == cg.CoreTypes.PhpAlias)
                {
                    // <place>.Value = <value>
                    cg.EmitConvertToPhpValue(valueType, 0);
                    cg.Emit_PhpAlias_SetValue();
                }
                else if (type == cg.CoreTypes.PhpValue)
                {
                    if (_thint.IsRef)
                    {
                        // Operators.SetValue(ref <place>, (PhpValue)<value>);
                        cg.EmitConvertToPhpValue(valueType, 0);
                        cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.SetValue_PhpValueRef_PhpValue);
                    }
                    else
                    {
                        // <place> = <value>
                        cg.EmitConvertToPhpValue(valueType, 0);
                        _place.EmitStore(cg.Builder);
                    }
                }
                else
                {
                    cg.EmitConvert(valueType, 0, type);
                    _place.EmitStore(cg.Builder);
                }
            }
        }

        #region IPlace

        public TypeSymbol TypeOpt => _place.TypeOpt;

        public bool HasAddress => _place.HasAddress;

        TypeSymbol IPlace.EmitLoad(ILBuilder il) => _place.EmitLoad(il);

        void IPlace.EmitStorePrepare(ILBuilder il) => _place.EmitStorePrepare(il);

        void IPlace.EmitStore(ILBuilder il) => _place.EmitStore(il);

        void IPlace.EmitLoadAddress(ILBuilder il) => _place.EmitLoadAddress(il);

        #endregion
    }

    internal class BoundSuperglobalPlace : IBoundReference
    {
        readonly VariableName _name;
        readonly BoundAccess _access;

        public BoundSuperglobalPlace(VariableName name, BoundAccess access)
        {
            Debug.Assert(name.IsAutoGlobal);
            _name = name;
            _access = access;
        }

        #region IBoundReference

        public TypeSymbol TypeOpt => null;  // PhpArray

        public void EmitLoadPrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt = null)
        {
            // nothing
        }

        public void EmitStorePrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt = null)
        {
            // Context
            cg.EmitLoadContext();
        }

        public TypeSymbol EmitLoad(CodeGenerator cg)
        {
            TypeSymbol result;

            if (_access.IsReadRef)
            {
                // TODO: update Context
                // &$<_name>
                // Template: ctx.Globals.EnsureAlias(<_name>)
                cg.EmitLoadContext();
                cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Context.Globals.Getter);
                cg.EmitIntStringKey(_name.Value);
                result = cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpArray.EnsureItemAlias_IntStringKey)
                    .Expect(cg.CoreTypes.PhpAlias);
            }
            else
            {
                Debug.Assert(_access.IsRead);
                var p = ResolveSuperglobalProperty(cg);
                cg.EmitLoadContext();
                result = cg.EmitCall(p.GetMethod.IsVirtual ? ILOpCode.Callvirt : ILOpCode.Call, p.GetMethod);

                //
                if (_access.IsReadValueCopy)
                {
                    result = cg.EmitDeepCopy(result, false);
                }
            }

            return result;
        }

        public void EmitStore(CodeGenerator cg, TypeSymbol valueType)
        {
            var p = ResolveSuperglobalProperty(cg);

            if (_access.IsUnset)
            {
                Debug.Assert(valueType == null);
                cg.EmitLoadDefault(p.Type, 0);
            }
            else
            {
                Debug.Assert(_access.IsWrite);
                cg.EmitConvert(valueType, 0, p.Type);
            }

            cg.EmitCall(p.SetMethod.IsVirtual ? ILOpCode.Callvirt : ILOpCode.Call, p.SetMethod);
        }

        #endregion

        PropertySymbol ResolveSuperglobalProperty(CodeGenerator cg)
        {
            PropertySymbol prop;

            var c = cg.CoreMethods.Context;

            if (_name == VariableName.GlobalsName) prop = c.Globals;
            else if (_name == VariableName.ServerName) prop = c.Server;
            else if (_name == VariableName.RequestName) prop = c.Request;
            else if (_name == VariableName.GetName) prop = c.Get;
            else if (_name == VariableName.PostName) prop = c.Post;
            else if (_name == VariableName.CookieName) prop = c.Cookie;
            else if (_name == VariableName.EnvName) prop = c.Env;
            else if (_name == VariableName.FilesName) prop = c.Files;
            else if (_name == VariableName.SessionName) prop = c.Session;
            else if (_name == VariableName.HttpRawPostDataName) prop = c.HttpRawPostData;
            else throw cg.NotImplementedException($"Superglobal ${_name.Value}");

            return prop;
        }
    }

    internal class BoundIndirectVariablePlace : IBoundReference
    {
        readonly BoundExpression _nameExpr;
        readonly BoundAccess _access;

        public BoundIndirectVariablePlace(BoundExpression nameExpr, BoundAccess access)
        {
            Contract.ThrowIfNull(nameExpr);
            _nameExpr = nameExpr;
            _access = access;
        }

        /// <summary>
        /// Loads reference to <c>PhpArray</c> containing variables.
        /// </summary>
        /// <returns><c>PhpArray</c> type symbol.</returns>
        protected virtual TypeSymbol LoadVariablesArray(CodeGenerator cg)
        {
            Debug.Assert(cg.LocalsPlaceOpt != null);

            // <locals>
            return cg.LocalsPlaceOpt.EmitLoad(cg.Builder)
                .Expect(cg.CoreTypes.PhpArray);
        }

        #region IBoundReference

        public TypeSymbol TypeOpt => null;

        private void EmitPrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt = null)
        {
            // Template: <variables> Key

            LoadVariablesArray(cg);

            // key
            cg.EmitIntStringKey(_nameExpr);
        }

        public void EmitLoadPrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt = null)
        {
            EmitPrepare(cg, instanceOpt);
        }

        public TypeSymbol EmitLoad(CodeGenerator cg)
        {
            // STACK: <PhpArray> <key>

            if (_access.EnsureObject)
            {
                // <array>.EnsureItemObject(<key>)
                return cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.PhpArray.EnsureItemObject_IntStringKey);
            }
            else if (_access.EnsureArray)
            {
                return cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.PhpArray.EnsureItemArray_IntStringKey);
            }
            else if (_access.IsReadRef)
            {
                return cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.PhpArray.EnsureItemAlias_IntStringKey);
            }
            else
            {
                // <array>.GetItemValue(<Index>)
                Debug.Assert(_access.IsRead);
                var result = cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.PhpArray.GetItemValue_IntStringKey);

                // 
                if (_access.IsReadValueCopy)
                {
                    // .GetValue()
                    result = cg.EmitDereference(result);

                    // .DeepCopy()
                    if (_access.TargetType == null || cg.IsCopiable(_access.TargetType))
                    {
                        result = cg.EmitDeepCopy(result);
                    }
                }

                //
                return result;
            }
        }

        public void EmitStorePrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt = null)
        {
            EmitPrepare(cg, instanceOpt);
        }

        public virtual void EmitStore(CodeGenerator cg, TypeSymbol valueType)
        {
            // STACK: <PhpArray> <key>

            if (_access.IsWriteRef)
            {
                // PhpAlias
                if (valueType != cg.CoreTypes.PhpAlias)
                {
                    cg.EmitConvertToPhpValue(valueType, 0);
                    cg.Emit_PhpValue_MakeAlias();
                }

                // .SetItemAlias(key, alias)
                cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.PhpArray.SetItemAlias_IntStringKey_PhpAlias);
            }
            else if (_access.IsUnset)
            {
                Debug.Assert(valueType == null);

                // .RemoveKey(key)
                cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.PhpArray.RemoveKey_IntStringKey);
            }
            else
            {
                Debug.Assert(_access.IsWrite);

                cg.EmitConvertToPhpValue(valueType, 0);

                // .SetItemValue(key, value)
                cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.PhpArray.SetItemValue_IntStringKey_PhpValue);
            }
        }

        #endregion
    }

    internal class BoundGlobalPlace : BoundIndirectVariablePlace
    {
        public BoundGlobalPlace(BoundExpression nameExpr, BoundAccess access)
            : base(nameExpr, access)
        {
        }

        protected override TypeSymbol LoadVariablesArray(CodeGenerator cg)
        {
            // $GLOBALS
            cg.EmitLoadContext();
            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Context.Globals.Getter);   // <ctx>.Globals
        }
    }

    internal sealed class BoundIndirectTemporalVariablePlace : BoundIndirectVariablePlace
    {
        public BoundIndirectTemporalVariablePlace(BoundExpression nameExpr, BoundAccess access)
            : base(nameExpr, access)
        {
        }

        protected override TypeSymbol LoadVariablesArray(CodeGenerator cg)
        {
            Debug.Assert(cg.TemporalLocalsPlace != null, $"Method with temporal variables must have {nameof(cg.TemporalLocalsPlace)} set.");

            return cg.TemporalLocalsPlace.EmitLoad(cg.Builder)
                .Expect(cg.CoreTypes.PhpArray);
        }

    }

    #endregion

    #region BoundFieldPlace, BoundIndirectFieldPlace, BoundPropertyPlace

    internal class BoundPropertyPlace : IBoundReference
    {
        readonly BoundExpression _instance;
        readonly PropertySymbol _property;

        public BoundPropertyPlace(BoundExpression instance, Cci.IPropertyDefinition property)
        {
            Contract.ThrowIfNull(property);

            _instance = instance;
            _property = (PropertySymbol)property;
        }

        /// <summary>
        /// Emits instance of the field containing class.
        /// </summary>
        protected void EmitLoadTarget(CodeGenerator cg, InstanceCacheHolder instanceOpt)
        {
            // instance
            var instancetype = InstanceCacheHolder.EmitInstance(instanceOpt, cg, _instance);

            //
            if (_property.IsStatic)
            {
                if (instancetype != null)
                {
                    cg.EmitPop(instancetype);
                }
            }
            else
            {
                if (instancetype != null)
                {
                    cg.EmitConvert(instancetype, _instance.TypeRefMask, _property.ContainingType);

                    if (_property.ContainingType.IsValueType)
                    {
                        throw new NotImplementedException(); // cg.EmitStructAddr(_property.ContainingType);   // value -> valueaddr
                    }
                }
                else
                {
                    throw cg.NotImplementedException($"Non-static property {_property.ContainingType.Name}::${_property.MetadataName} accessed statically!", _instance);
                }
            }
        }

        public TypeSymbol TypeOpt => _property.Type;

        public bool HasAddress => false;

        public TypeSymbol EmitLoad(CodeGenerator cg)
        {
            //if (_property.Getter == null)
            //    throw new InvalidOperationException();

            var getter = _property.GetMethod;
            return cg.EmitCall(getter.IsVirtual ? ILOpCode.Callvirt : ILOpCode.Call, getter);
        }

        public void EmitLoadAddress(CodeGenerator cg)
        {
            throw new NotSupportedException();
        }

        public void EmitStore(CodeGenerator cg, TypeSymbol valueType)
        {
            //if (_property.Setter == null)
            //    throw new InvalidOperationException();

            var setter = _property.SetMethod;

            cg.EmitConvert(valueType, 0, setter.Parameters[0].Type);
            cg.EmitCall(setter.IsVirtual ? ILOpCode.Callvirt : ILOpCode.Call, setter);
        }

        public void EmitLoadPrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt)
        {
            if (_property == null)
            {
                // TODO: callsite.Target callsite
                throw new NotImplementedException();
            }

            EmitLoadTarget(cg, instanceOpt);
        }

        public void EmitStorePrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt)
        {
            if (_property == null)
            {
                // TODO: callsite.Target callsite
                throw new NotImplementedException();
            }

            EmitLoadTarget(cg, instanceOpt);
        }

        public void EmitUnset(CodeGenerator cg)
        {
            var bound = (IBoundReference)this;
            bound.EmitStorePrepare(cg);
            bound.EmitStore(cg, cg.Emit_PhpValue_Void());
        }
    }

    /// <summary>
    /// A direct field access.
    /// </summary>
    internal class BoundFieldPlace : IBoundReference
    {
        public FieldSymbol Field => _field;
        readonly FieldSymbol _field;

        public BoundExpression Instance => _instance;
        readonly BoundExpression _instance;

        readonly BoundExpression _boundref;

        protected BoundAccess Access => _boundref.Access;

        public BoundFieldPlace(BoundExpression instance, FieldSymbol field, BoundExpression boundref)
        {
            Contract.ThrowIfNull(field);

            field = (FieldSymbol)FieldPlace.GetRealDefinition(field);

            _instance = instance;
            _field = field;
            _boundref = boundref;
        }

        #region IBoundReference

        /// <summary>
        /// Emits ldfld, stfld, ldflda, ldsfld, stsfld.
        /// </summary>
        /// <param name="cg"></param>
        /// <param name="code">ld* or st* OP code.</param>
        void EmitOpCode(CodeGenerator cg, ILOpCode code)
        {
            Debug.Assert(_field != null);
            cg.Builder.EmitOpCode(code);
            cg.EmitSymbolToken(_field, null);
        }

        public bool HasAddress => true;

        public TypeSymbol TypeOpt => _field.Type;

        TypeSymbol EmitOpCode_Load(CodeGenerator cg)
        {
            EmitOpCode(cg, Field.IsStatic ? ILOpCode.Ldsfld : ILOpCode.Ldfld);
            return _field.Type;
        }

        void EmitOpCode_LoadAddress(CodeGenerator cg)
        {
            EmitOpCode(cg, Field.IsStatic ? ILOpCode.Ldsflda : ILOpCode.Ldflda);
        }

        void EmitOpCode_Store(CodeGenerator cg)
        {
            EmitOpCode(cg, Field.IsStatic ? ILOpCode.Stsfld : ILOpCode.Stfld);
        }

        public static TypeSymbol EmitLoadTarget(CodeGenerator cg, FieldSymbol field, TypeSymbol instancetype, TypeRefMask instanceTypeHint = default(TypeRefMask))
        {
            //
            if (field.IsStatic)
            {
                if (instancetype != null)
                {
                    cg.EmitPop(instancetype);
                }

                return null;
            }
            else
            {
                var statics = field.ContainingStaticsHolder();   // in case field is a PHP static field
                if (statics != null)
                {
                    // PHP static field contained in a holder class
                    if (instancetype != null)
                    {
                        cg.EmitPop(instancetype);
                        instancetype = null;
                    }

                    // Template: <ctx>.GetStatics<_statics>()
                    cg.EmitLoadContext();
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Context.GetStatic_T.Symbol.Construct(statics)).Expect(statics);
                }
                else
                {
                    if (instancetype != null)
                    {
                        cg.EmitConvert(instancetype, instanceTypeHint, field.ContainingType);

                        if (field.ContainingType.IsValueType)
                        {
                            throw new NotImplementedException(); // cg.EmitStructAddr(_field.ContainingType);   // value -> valueaddr
                        }

                        return field.ContainingType;
                    }
                    else
                    {
                        throw cg.NotImplementedException($"Non-static field {field.ContainingType.Name}::${field.MetadataName} accessed statically!");
                    }
                }
            }
        }

        /// <summary>
        /// Emits instance of the field containing class.
        /// </summary>
        protected void EmitLoadTarget(CodeGenerator cg, InstanceCacheHolder instanceOpt)
        {
            // instance
            var instancetype = InstanceCacheHolder.EmitInstance(instanceOpt, cg, Instance);

            // instance ?? proper field target:
            EmitLoadTarget(cg, _field, instancetype,
                instanceTypeHint: (Instance != null) ? Instance.TypeRefMask : 0);
        }

        public void EmitLoadPrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt)
        {
            EmitLoadTarget(cg, instanceOpt);
        }

        public virtual TypeSymbol EmitLoad(CodeGenerator cg)
        {
            Debug.Assert(Access.IsRead);

            if (Field.IsConst)
            {
                Debug.Assert(this.Access.IsRead && !this.Access.IsEnsure);
                Debug.Assert(this.Field.HasConstantValue);

                return cg.EmitLoadConstant(Field.ConstantValue);
            }

            var type = Field.Type;

            // Ensure Object (..->Field->.. =)
            if (Access.EnsureObject)
            {
                if (type == cg.CoreTypes.PhpAlias)
                {
                    EmitOpCode_Load(cg);    // PhpAlias
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpAlias.EnsureObject)
                        .Expect(SpecialType.System_Object);
                }
                else if (type == cg.CoreTypes.PhpValue)
                {
                    EmitOpCode_LoadAddress(cg); // &PhpValue
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.EnsureObject)
                        .Expect(SpecialType.System_Object);
                }
                else
                {
                    if (type.IsReferenceType)
                    {
                        // TODO: ensure it is not null
                        EmitOpCode_Load(cg);
                        return type;
                    }
                    else
                    {
                        // return new stdClass(ctx)
                        throw new NotImplementedException();
                    }
                }
            }
            // Ensure Array (xxx->Field[] =)
            else if (Access.EnsureArray)
            {
                if (type == cg.CoreTypes.PhpAlias)
                {
                    EmitOpCode_Load(cg);
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpAlias.EnsureArray)
                        .Expect(cg.CoreTypes.IPhpArray);
                }
                else if (type == cg.CoreTypes.PhpValue)
                {
                    EmitOpCode_LoadAddress(cg);
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.EnsureArray)
                            .Expect(cg.CoreTypes.IPhpArray);
                }
                else if (type == cg.CoreTypes.PhpString)
                {
                    Debug.Assert(type.IsValueType);
                    // <place>.EnsureWritable() : IPhpArray
                    EmitOpCode_LoadAddress(cg); // LOAD ref PhpString
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpString.EnsureWritable);
                }
                else if (type.IsOfType(cg.CoreTypes.IPhpArray))
                {
                    EmitOpCode_LoadAddress(cg); // ensure value is not null
                    if (type == cg.CoreTypes.PhpArray)
                    {
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.EnsureArray_PhpArrayRef)
                           .Expect(cg.CoreTypes.PhpArray);
                    }
                    else
                    {
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.EnsureArray_IPhpArrayRef)
                            .Expect(cg.CoreTypes.IPhpArray);
                    }
                }

                throw new NotImplementedException();
            }
            // Ensure Alias (&...->Field)
            else if (Access.IsReadRef)
            {
                if (type == cg.CoreTypes.PhpAlias)
                {
                    // TODO: <place>.AddRef()
                    EmitOpCode_Load(cg);
                    return type;
                }
                else if (type == cg.CoreTypes.PhpValue)
                {
                    // return <place>.EnsureAlias()
                    EmitOpCode_LoadAddress(cg); // &PhpValue
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.EnsureAlias)
                        .Expect(cg.CoreTypes.PhpAlias);
                }
                else
                {
                    cg.Diagnostics.Add(cg.Routine, _boundref.PhpSyntax, Errors.ErrorCode.ERR_ValueOfTypeCannotBeAliased, type.Name);

                    // new PhpAlias((PhpValue)<place>, 1)
                    EmitOpCode_Load(cg);
                    cg.EmitConvertToPhpValue(type, 0);
                    return cg.Emit_PhpValue_MakeAlias();
                }
            }
            // Read by value copy (copy value if applicable)
            else if (Access.IsReadValueCopy)
            {
                // dereference & copy

                // if target type is not a copiable type, we don't have to perform deep copy since the result will be converted to a value anyway
                bool deepcopy = Access.TargetType == null || cg.IsCopiable(Access.TargetType);

                if (type == cg.CoreTypes.PhpValue)
                {
                    // ref.GetValue().DeepCopy()
                    EmitOpCode_LoadAddress(cg);
                    var t = cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.GetValue);
                    return deepcopy ? cg.EmitDeepCopy(t) : t;
                }
                else if (type == cg.CoreTypes.PhpAlias)
                {
                    // ref.Value.DeepCopy()
                    EmitOpCode_Load(cg);
                    cg.Emit_PhpAlias_GetValueAddr();
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.DeepCopy);
                }

                EmitOpCode_Load(cg);
                return deepcopy ? cg.EmitDeepCopy(type) : type;
            }
            else
            {
                // read directly PhpValue into the target type (skips eventual dereferencing and copying)
                if (Access.TargetType != null && type == cg.CoreTypes.PhpValue)
                {
                    // convert PhpValue to target type without loading whole value and storing to temporary variable
                    switch (Access.TargetType.SpecialType)
                    {
                        case SpecialType.System_Double:
                            EmitOpCode_LoadAddress(cg); // &PhpValue.ToDouble()
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.ToDouble);
                        case SpecialType.System_Int64:
                            EmitOpCode_LoadAddress(cg); // &PhpValue.ToLong()
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.ToLong);
                        case SpecialType.System_Boolean:
                            EmitOpCode_LoadAddress(cg); // &PhpValue.ToBoolean()
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.ToBoolean);
                        case SpecialType.System_String:
                            EmitOpCode_LoadAddress(cg); // &PhpValue.ToString(ctx)
                            cg.EmitLoadContext();
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.ToString_Context);
                        case SpecialType.System_Object:
                            EmitOpCode_LoadAddress(cg); // &PhpValue.ToClass()
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.ToClass);
                        default:
                            if (Access.TargetType == cg.CoreTypes.PhpArray)
                            {
                                EmitOpCode_LoadAddress(cg); // &PhpValue.ToArray()
                                return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.ToArray);
                            }
                            break;
                    }
                }

                // 

                if (Access.IsReadValue)
                {
                    // dereference

                    if (type == cg.CoreTypes.PhpAlias)
                    {
                        EmitOpCode_Load(cg);
                        return cg.Emit_PhpAlias_GetValue();
                    }
                    else if (type == cg.CoreTypes.PhpValue)
                    {
                        // Template (ref PhpValue).GetValue()
                        EmitOpCode_LoadAddress(cg);
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.GetValue);
                    }
                }

                //
                // Read (...->Field)
                //

                EmitOpCode_Load(cg);
                return type;
            }
        }

        public void EmitStorePrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt)
        {
            Debug.Assert(Access.IsWrite || Access.IsUnset);

            EmitLoadTarget(cg, instanceOpt);

            //
            var type = Field.Type;

            if (Access.IsWriteRef)
            {
                // no need for preparation
            }
            else if (Access.IsUnset)
            {
                // no need for preparation
            }
            else
            {
                //
                if (type == cg.CoreTypes.PhpAlias)
                {
                    // (PhpAlias)<place>
                    EmitOpCode_Load(cg);    // PhpAlias
                }
                else if (type == cg.CoreTypes.PhpValue)
                {
                    EmitOpCode_LoadAddress(cg); // &PhpValue
                }
                else
                {
                    // no need for preparation
                }
            }
        }

        public void EmitStore(CodeGenerator cg, TypeSymbol valueType)
        {
            Debug.Assert(Access.IsWrite || Access.IsUnset);

            var type = Field.Type;

            if (Access.IsWriteRef)
            {
                if (valueType != cg.CoreTypes.PhpAlias)
                {
                    Debug.Assert(false, "caller should get aliased value");
                    cg.EmitConvertToPhpValue(valueType, 0);
                    valueType = cg.Emit_PhpValue_MakeAlias();
                }

                //
                if (type == cg.CoreTypes.PhpAlias)
                {
                    // <place> = <alias>
                    EmitOpCode_Store(cg);
                }
                else if (type == cg.CoreTypes.PhpValue)
                {
                    // <place> = PhpValue.Create(<alias>)
                    cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.Create_PhpAlias);
                    EmitOpCode_Store(cg);
                }
                else
                {
                    Debug.Assert(false, "Assigning alias to non-aliasable field.");
                    cg.EmitConvert(valueType, 0, type);
                    EmitOpCode_Store(cg);
                }
            }
            else if (Access.IsUnset)
            {
                Debug.Assert(valueType == null);

                // <place> = default(T)

                if (type == cg.CoreTypes.PhpAlias)
                {
                    // new PhpAlias(void)
                    cg.Emit_PhpValue_Void();
                    cg.Emit_PhpValue_MakeAlias();
                }
                else if (type.IsReferenceType)
                {
                    // null
                    cg.Builder.EmitNullConstant();
                }
                else
                {
                    // default(T)
                    cg.EmitLoadDefaultOfValueType(type);
                }

                EmitOpCode_Store(cg);
            }
            else
            {
                //
                if (type == cg.CoreTypes.PhpAlias)
                {
                    // <Field>.Value = <value>
                    cg.EmitConvertToPhpValue(valueType, 0);
                    cg.Emit_PhpAlias_SetValue();
                }
                else if (type == cg.CoreTypes.PhpValue)
                {
                    // Operators.SetValue(ref <Field>, (PhpValue)<value>);
                    cg.EmitConvertToPhpValue(valueType, 0);
                    cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.SetValue_PhpValueRef_PhpValue);
                }
                else
                {
                    cg.EmitConvert(valueType, 0, type);
                    EmitOpCode_Store(cg);
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Indirect field access using callsites.
    /// </summary>
    internal class BoundIndirectFieldPlace : IBoundReference
    {
        readonly BoundFieldRef _field;

        public BoundVariableName Name => _field.FieldName;
        public BoundExpression Instance => _field.Instance;
        public string NameValueOpt => _field.FieldName.NameValue.Value;
        public BoundAccess Access => _field.Access;

        public BoundIndirectFieldPlace(BoundFieldRef field)
        {
            Contract.ThrowIfNull(field);
            _field = field;
        }

        #region IBoundReference

        DynamicOperationFactory.CallSiteData _lazyLoadCallSite = null;
        DynamicOperationFactory.CallSiteData _lazyStoreCallSite = null;

        public TypeSymbol TypeOpt => null;

        public void EmitLoadPrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt = null)
        {
            if (_lazyLoadCallSite == null)
                _lazyLoadCallSite = cg.Factory.StartCallSite("get_" + this.NameValueOpt);

            _lazyLoadCallSite.Prepare(cg);

            // callsite.Target callsite
            _lazyLoadCallSite.EmitLoadTarget();
            _lazyLoadCallSite.EmitLoadCallsite();

            // target instance
            _lazyLoadCallSite.EmitTargetInstance(_cg => InstanceCacheHolder.EmitInstance(instanceOpt, _cg, Instance));
        }

        public TypeSymbol EmitLoad(CodeGenerator cg)
        {
            Debug.Assert(_lazyLoadCallSite != null);
            Debug.Assert(this.Instance.ResultType != null);

            // resolve actual return type
            TypeSymbol return_type;
            if (Access.EnsureObject) return_type = cg.CoreTypes.Object;
            else if (Access.EnsureArray) return_type = cg.CoreTypes.IPhpArray;
            else if (Access.IsReadRef) return_type = cg.CoreTypes.PhpAlias;
            else if (Access.IsIsSet) return_type = cg.CoreTypes.Boolean;
            else return_type = Access.TargetType ?? cg.CoreTypes.PhpValue;

            // Template: Invoke(TInstance, Context, [string name])

            _lazyLoadCallSite.EmitLoadContext();                    // ctx : Context
            _lazyLoadCallSite.EmitNameParam(Name.NameExpression);   // [name] : string
            _lazyLoadCallSite.EmitCallerTypeParam();                // [classctx] : RuntimeTypeHandle

            // Target()
            var functype = cg.Factory.GetCallSiteDelegateType(
                null, RefKind.None,
                _lazyLoadCallSite.Arguments,
                default(ImmutableArray<RefKind>),
                null,
                return_type);

            cg.EmitCall(ILOpCode.Callvirt, functype.DelegateInvokeMethod);

            //
            _lazyLoadCallSite.Construct(functype, cctor =>
            {
                // new GetFieldBinder(field_name, context, return, flags)
                cctor.Builder.EmitStringConstant(this.NameValueOpt);
                cctor.EmitLoadToken(cg.CallerType, null);           // class context
                cctor.EmitLoadToken(return_type, null);
                cctor.Builder.EmitIntConstant((int)Access.Flags);
                cctor.EmitCall(ILOpCode.Call, cg.CoreMethods.Dynamic.GetFieldBinder);
            });

            //
            return return_type;
        }

        public void EmitStorePrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt = null)
        {
            if (_lazyStoreCallSite == null)
                _lazyStoreCallSite = cg.Factory.StartCallSite("set_" + this.NameValueOpt);

            _lazyStoreCallSite.Prepare(cg);

            // callsite.Target callsite
            _lazyStoreCallSite.EmitLoadTarget();
            _lazyStoreCallSite.EmitLoadCallsite();

            // target instance
            _lazyStoreCallSite.EmitTargetInstance(_cg => InstanceCacheHolder.EmitInstance(instanceOpt, _cg, Instance));

            // ctx : Context
            _lazyStoreCallSite.EmitLoadContext();

            // [name] : string
            _lazyStoreCallSite.EmitNameParam(Name.NameExpression);

            // [classctx] : RuntimeTypeHandle
            _lazyStoreCallSite.EmitCallerTypeParam();                // [classctx] : RuntimeTypeHandle
        }

        public void EmitStore(CodeGenerator cg, TypeSymbol valueType)
        {
            Debug.Assert(_lazyStoreCallSite != null);
            Debug.Assert(this.Instance.ResultType != null);

            // Template: Invoke(TInstance, Context, [string name], [value])

            if (valueType != null)
            {
                _lazyStoreCallSite.AddArg(valueType);
            }

            // Target()
            var functype = cg.Factory.GetCallSiteDelegateType(
                null, RefKind.None,
                _lazyStoreCallSite.Arguments,
                default(ImmutableArray<RefKind>),
                null,
                cg.CoreTypes.Void);

            cg.EmitCall(ILOpCode.Callvirt, functype.DelegateInvokeMethod);

            _lazyStoreCallSite.Construct(functype, cctor =>
            {
                cctor.Builder.EmitStringConstant(this.NameValueOpt);
                cctor.EmitLoadToken(cg.CallerType, null);           // class context
                cctor.Builder.EmitIntConstant((int)Access.Flags);   // flags
                cctor.EmitCall(ILOpCode.Call, cg.CoreMethods.Dynamic.SetFieldBinder);
            });
        }

        #endregion
    }

    /// <summary>
    /// Indirect static or constant field access using callsites.
    /// </summary>
    internal class BoundIndirectStFieldPlace : IBoundReference
    {
        public BoundVariableName Name => _name;
        readonly BoundVariableName _name;

        public BoundTypeRef Type => _type;
        readonly BoundTypeRef _type;

        public BoundAccess Access => _boundref.Access;
        readonly BoundFieldRef _boundref;

        public bool IsConstant => _boundref.IsClassConstant;

        public string NameValueOpt => _name.IsDirect ? _name.NameValue.Value : null;

        public BoundIndirectStFieldPlace(BoundTypeRef typeref, BoundVariableName fldname, BoundFieldRef boundref)
        {
            Debug.Assert(boundref != null, nameof(boundref));

            _type = typeref;
            _name = fldname;
            _boundref = boundref;
        }

        #region IBoundReference

        DynamicOperationFactory.CallSiteData _lazyLoadCallSite = null;
        DynamicOperationFactory.CallSiteData _lazyStoreCallSite = null;

        public TypeSymbol TypeOpt => null;

        public void EmitLoadPrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt = null)
        {
            if (_lazyLoadCallSite == null)
                _lazyLoadCallSite = cg.Factory.StartCallSite("get_" + this.NameValueOpt);

            _lazyLoadCallSite.Prepare(cg);

            // callsite.Target callsite
            _lazyLoadCallSite.EmitLoadTarget();
            _lazyLoadCallSite.EmitLoadCallsite();

            // LOAD PhpTypeInfo
            _lazyLoadCallSite.EmitTargetTypeParam(_type);
        }

        public TypeSymbol EmitLoad(CodeGenerator cg)
        {
            // resolve actual return type
            TypeSymbol return_type;
            if (Access.EnsureObject) return_type = cg.CoreTypes.Object;
            else if (Access.EnsureArray) return_type = cg.CoreTypes.IPhpArray;
            else if (Access.IsReadRef) return_type = cg.CoreTypes.PhpAlias;
            else if (Access.IsIsSet) return_type = cg.CoreTypes.Boolean;
            else return_type = Access.TargetType ?? cg.CoreTypes.PhpValue;

            // Template: Invoke(PhpTypeInfo, Context, [string name])

            _lazyLoadCallSite.EmitLoadContext();                                    // ctx : Context
            _lazyLoadCallSite.EmitNameParam(Name.NameExpression);                   // [name] : string

            // Target()
            var functype = cg.Factory.GetCallSiteDelegateType(
                null, RefKind.None,
                _lazyLoadCallSite.Arguments,
                default(ImmutableArray<RefKind>),
                null,
                return_type);

            cg.EmitCall(ILOpCode.Callvirt, functype.DelegateInvokeMethod);

            //
            _lazyLoadCallSite.Construct(functype, cctor =>
            {
                Debug.Assert(cctor.Routine == cg.Routine);  // same caller context

                // [GetFieldBinder|GetClassConstBinder](field_name, context, return, flags)
                cctor.Builder.EmitStringConstant(this.NameValueOpt);
                cctor.EmitLoadToken(cg.CallerType, null);           // class context
                cctor.EmitLoadToken(return_type, null);
                cctor.Builder.EmitIntConstant((int)Access.Flags);
                cctor.EmitCall(ILOpCode.Call, _boundref.IsClassConstant
                    ? cg.CoreMethods.Dynamic.GetClassConstBinder
                    : cg.CoreMethods.Dynamic.GetFieldBinder);
            });

            //
            return return_type;
        }

        public void EmitStorePrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt = null)
        {
            if (_lazyStoreCallSite == null)
                _lazyStoreCallSite = cg.Factory.StartCallSite("set_" + this.NameValueOpt);

            _lazyStoreCallSite.Prepare(cg);

            // callsite.Target callsite
            _lazyStoreCallSite.EmitLoadTarget();
            _lazyStoreCallSite.EmitLoadCallsite();

            // LOAD PhpTypeInfo
            _lazyStoreCallSite.EmitTargetTypeParam(_type);

            // Context
            _lazyStoreCallSite.EmitLoadContext();

            // NameExpression in case of indirect call
            _lazyStoreCallSite.EmitNameParam(_name.NameExpression);
        }

        public void EmitStore(CodeGenerator cg, TypeSymbol valueType)
        {
            Debug.Assert(_lazyStoreCallSite != null);

            // Template: Invoke(PhpTypeInfo, Context, [string name], [value])

            if (valueType != null)
            {
                _lazyStoreCallSite.AddArg(valueType);
            }

            // Target()
            var functype = cg.Factory.GetCallSiteDelegateType(
                null, RefKind.None,
                _lazyStoreCallSite.Arguments,
                default(ImmutableArray<RefKind>),
                null,
                cg.CoreTypes.Void);

            cg.EmitCall(ILOpCode.Callvirt, functype.DelegateInvokeMethod);

            _lazyStoreCallSite.Construct(functype, cctor =>
            {
                cctor.Builder.EmitStringConstant(this.NameValueOpt);
                cctor.EmitCallerTypeHandle();
                cctor.Builder.EmitIntConstant((int)Access.Flags);   // flags
                cctor.EmitCall(ILOpCode.Call, cg.CoreMethods.Dynamic.SetFieldBinder);
            });
        }

        #endregion
    }

    #endregion
}
