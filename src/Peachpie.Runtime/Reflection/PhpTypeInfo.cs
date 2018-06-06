﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Pchp.Core.Reflection
{
    #region PhpTypeInfo

    /// <summary>
    /// Runtime information about a type.
    /// </summary>
    [DebuggerDisplay("{Name,nq}")]
    [DebuggerNonUserCode]
    public class PhpTypeInfo
    {
        /// <summary>
        /// Index to the type slot.
        /// <c>0</c> is an uninitialized index.
        /// </summary>
        internal int Index { get { return _index; } set { _index = value; } }
        protected int _index;

        /// <summary>
        /// Gets value indicating the type was declared in a users code.
        /// Otherwise the type is from a library.
        /// </summary>
        public bool IsUserType => _index > 0;

        /// <summary>
        /// Gets value indicating the type is an interface.
        /// </summary>
        public bool IsInterface => _type.IsInterface;

        /// <summary>
        /// Gets value indicating the type is a trait.
        /// </summary>
        public bool IsTrait => !IsInterface && _type.IsSealed && _type.GetCustomAttribute<PhpTraitAttribute>(false) != null;

        /// <summary>
        /// Gets list of PHP extensions associated with the current type.
        /// </summary>
        public string[] Extensions => _type.GetCustomAttribute<PhpExtensionAttribute>(false)?.Extensions ?? Array.Empty<string>();

        /// <summary>
        /// Gets the full type name in PHP syntax, cannot be <c>null</c> or empty.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the relative path to the file where the type is declared.
        /// Gets <c>null</c> if the type is from core, eval etc.
        /// </summary>
        public string RelativePath => _type.GetCustomAttribute<PhpTypeAttribute>(false)?.FileName;

        /// <summary>
        /// CLR type declaration.
        /// </summary>
        public Type Type => _type;
        readonly Type _type;

        /// <summary>
        /// Gets <see cref="RuntimeTypeHandle"/> of corresponding type information.
        /// </summary>
        public RuntimeTypeHandle TypeHandle => _type.UnderlyingSystemType.TypeHandle;

        /// <summary>
        /// Dynamically constructed delegate for object creation.
        /// </summary>
        public TObjectCreator Creator => _lazyCreator ?? BuildCreator();

        /// <summary>
        /// Creates instance of the class without invoking its constructor.
        /// </summary>
        public object GetUninitializedInstance(Context ctx)
        {
            if (_lazyEmptyCreator == null)
            {
                _lazyEmptyCreator = TypeMembersUtils.BuildCreateEmptyObjectFunc(this);
            }

            return _lazyEmptyCreator(ctx);
        }
        Func<Context, object> _lazyEmptyCreator;

        TObjectCreator Creator_private => _lazyCreatorPrivate ?? BuildCreatorPrivate();
        TObjectCreator Creator_protected => _lazyCreatorProtected ?? BuildCreatorProtected();
        TObjectCreator _lazyCreator, _lazyCreatorPrivate, _lazyCreatorProtected;

        /// <summary>
        /// A delegate used for representing an inaccessible class constructor.
        /// </summary>
        public static TObjectCreator InaccessibleCreator => s_inaccessibleCreator;
        static readonly TObjectCreator s_inaccessibleCreator = (ctx, _) => { throw new MethodAccessException(); };

        /// <summary>
        /// Dynamically constructed delegate for object creation in specific type context.
        /// </summary>
        /// <param name="caller">Current type context in order to resolve only visible constructors.</param>
        public TObjectCreator ResolveCreator(Type caller)
        {
            if (caller != null)
            {
                if (caller == _type)
                {
                    // creation including private|protected|public .ctors
                    return this.Creator_private;
                }

                if (_lazyCreatorProtected == null || _lazyCreatorProtected != _lazyCreator) // in case protected creator == public creator, we can skip following checks
                {
                    if (caller.IsAssignableFrom(_type) || _type.IsAssignableFrom(caller))
                    {
                        // creation including protected|public .ctors
                        return this.Creator_protected;
                    }
                }
            }

            // creation using public .ctors
            return this.Creator;
        }

        /// <summary>
        /// Gets base type or <c>null</c> in case type does not extend another class.
        /// </summary>
        public PhpTypeInfo BaseType
        {
            get
            {
                if ((_flags & Flags.BaseTypePopulated) == 0)
                {
                    var binfo = _type.BaseType;
                    _lazyBaseType = (binfo != null && !binfo.IsHiddenType()) ? binfo.GetPhpTypeInfo() : null;
                    _flags |= Flags.BaseTypePopulated;
                }
                return _lazyBaseType;
            }
        }
        PhpTypeInfo _lazyBaseType;

        /// <summary>
        /// Build creation delegate using public .ctors.
        /// </summary>
        TObjectCreator BuildCreator()
        {
            lock (this)
            {
                if (_lazyCreator == null)
                {
                    var ctors = _type.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
                   
                    _lazyCreator = ctors.Length > 0 
                        ? Dynamic.BinderHelpers.BindToCreator(_type, ctors) 
                        : s_inaccessibleCreator;
                }
            }

            return _lazyCreator;
        }

        /// <summary>
        /// Build creation delegate using public, protected and private .ctors.
        /// </summary>
        TObjectCreator BuildCreatorPrivate()
        {
            lock (this)
            {
                if (_lazyCreatorPrivate == null)
                {
                    var ctorsList = new List<ConstructorInfo>();
                    bool hasPrivate = false;
                    foreach (var c in _type.GetConstructors(BindingFlags.Instance))
                    {
                        if (!c.IsPhpFieldsOnlyCtor())
                        {
                            ctorsList.Add(c);
                            hasPrivate |= c.IsPrivate;
                        }
                    }
                    _lazyCreatorPrivate = hasPrivate
                        ? Dynamic.BinderHelpers.BindToCreator(_type, ctorsList.ToArray())
                        : this.Creator_protected;
                }
            }

            return _lazyCreatorPrivate;
        }

        /// <summary>
        /// Build creation delegate using public and protected .ctors.
        /// </summary>
        TObjectCreator BuildCreatorProtected()
        {
            lock (this)
            {
                if (_lazyCreatorProtected == null)
                {
                    var ctorsList = new List<ConstructorInfo>();
                    bool hasProtected = false;
                    foreach (var c in _type.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
                    {
                        if (!c.IsPhpFieldsOnlyCtor())
                        {
                            ctorsList.Add(c);
                            hasProtected |= c.IsFamily;
                        }
                    }
                    _lazyCreatorProtected = hasProtected
                        ? Dynamic.BinderHelpers.BindToCreator(_type, ctorsList.ToArray())
                        : this.Creator;
                }
            }

            return _lazyCreatorProtected;
        }

        internal PhpTypeInfo(Type t)
        {
            Debug.Assert(t != null);
            _type = t;

            var attr = _type.GetCustomAttribute<PhpTypeAttribute>(false);
            Name = ResolvePhpTypeName(_type, attr);

            // register type in extension tables
            ExtensionsAppContext.ExtensionsTable.AddType(this);
        }
        
        /// <summary>
        /// Resolves PHP-like type name.
        /// </summary>
        static string ResolvePhpTypeName(Type type, PhpTypeAttribute attr)
        {
            string name;

            if (attr != null && (name = attr.ExplicitTypeName) != null)
            {
                // explicitly specified type name
                name = name.Replace(PhpTypeAttribute.InheritName, type.Name);
            }
            else
            {
                // CLR type
                name = type.FullName       // full PHP type name instead of CLR type name
                   .Replace('.', '\\')      // namespace separator
                   .Replace('+', '\\');     // nested type separator

                // remove suffixed indexes (after a special metadata character)
                var idx = name.IndexOfAny(_metadataSeparators);
                if (idx >= 0)
                {
                    name = name.Remove(idx);
                }
            }

            Debug.Assert(ReflectionUtils.IsAllowedPhpName(name));
            
            //
            return name;
        }

        /// <summary>
        /// Array of characters used to separate class name from its metadata indexes (order, generics, etc).
        /// These characters and suffixed text has to be ignored.
        /// </summary>
        private static readonly char[] _metadataSeparators = new[] { '#', '@', '`', '<', '?' };

        #region Reflection

        /// <summary>
        /// Various type info flags.
        /// </summary>
        [Flags]
        enum Flags
        {
            BaseTypePopulated = 64,
            RuntimeFieldsHolderPopulated = 128,
        }

        Flags _flags;

        /// <summary>
        /// Gets collection of PHP methods in this type and base types.
        /// </summary>
        public TypeMethods RuntimeMethods => _runtimeMethods ?? (_runtimeMethods = new TypeMethods(this));
        TypeMethods _runtimeMethods;

        /// <summary>
        /// Gets collection of PHP fields, static fields and constants declared in this type.
        /// </summary>
        public TypeFields DeclaredFields => _declaredfields ?? (_declaredfields = new TypeFields(_type));
        TypeFields _declaredfields;

        /// <summary>
        /// Gets field holding the array of runtime fields.
        /// Can be <c>null</c>.
        /// </summary>
        public FieldInfo RuntimeFieldsHolder
        {
            get
            {
                if ((_flags & Flags.RuntimeFieldsHolderPopulated) == 0)
                {
                    _runtimeFieldsHolder = Dynamic.BinderHelpers.LookupRuntimeFields(_type);
                    _flags |= Flags.RuntimeFieldsHolderPopulated;
                }
                return _runtimeFieldsHolder;
            }
        }
        FieldInfo _runtimeFieldsHolder;

        // TODO: PHPDoc

        #endregion
    }

    #endregion

    #region PhpTypeInfoExtension

    [DebuggerNonUserCode]
    public static class PhpTypeInfoExtension
    {
        /// <summary>
        /// <see cref="MethodInfo"/> of <see cref="PhpTypeInfoExtension.GetPhpTypeInfo{TType}"/>.
        /// </summary>
        readonly static MethodInfo s_getPhpTypeInfo_T = typeof(PhpTypeInfoExtension).GetRuntimeMethod("GetPhpTypeInfo", Dynamic.Cache.Types.Empty);

        /// <summary>
        /// Cache of resolved <see cref="PhpTypeInfo"/> corresponding to <see cref="RuntimeTypeHandle"/>.
        /// </summary>
        readonly static Dictionary<RuntimeTypeHandle, PhpTypeInfo> s_cache = new Dictionary<RuntimeTypeHandle, PhpTypeInfo>();

        /// <summary>
        /// Gets <see cref="PhpTypeInfo"/> of given <typeparamref name="TType"/>.
        /// </summary>
        /// <typeparam name="TType">Type to get info about.</typeparam>
        /// <returns>Runtime type information.</returns>
        public static PhpTypeInfo GetPhpTypeInfo<TType>()
            => TypeInfoHolder<TType>.TypeInfo;

        /// <summary>
        /// Gets <see cref="PhpTypeInfo"/> of given <paramref name="type"/>.
        /// </summary>
        public static PhpTypeInfo GetPhpTypeInfo(this Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            PhpTypeInfo result = null;
            var handle = type.TypeHandle;

            // lookup cache first
            lock (s_cache)    // TODO: RW lock
            {
                s_cache.TryGetValue(handle, out result);
            }

            // invoke GetPhpTypeInfo<TType>() dynamically and cache the result
            if (result == null)
            {
                if (type.IsGenericTypeDefinition)
                {
                    // generic type definition cannot be used as a type parameter for GetPhpTypeInfo<T>
                    // just instantiate the type info and cache the result
                    result = new PhpTypeInfo(type);
                }
                else
                {
                    // TypeInfoHolder<TType>.TypeInfo;
                    result = (PhpTypeInfo)s_getPhpTypeInfo_T
                        .MakeGenericMethod(type)
                        .Invoke(null, Utilities.ArrayUtils.EmptyObjects);
                }

                lock (s_cache)
                {
                    s_cache[handle] = result;
                }
            }

            //
            return result;
        }

        /// <summary>
        /// Gets <see cref="PhpTypeInfo"/> of given <paramref name="handle"/>.
        /// </summary>
        /// <param name="handle">Type handle of the CLR type.</param>
        public static PhpTypeInfo GetPhpTypeInfo(this RuntimeTypeHandle handle)
        {
            PhpTypeInfo result = null;

            if (handle.Equals(default(RuntimeTypeHandle)))
            {
                return null;
            }

            // lookup cache first
            lock (s_cache)   // TODO: RW lock
            {
                s_cache.TryGetValue(handle, out result);
            }

            return result ?? GetPhpTypeInfo(Type.GetTypeFromHandle(handle));
        }

        /// <summary>
        /// Gets <see cref="PhpTypeInfo"/> of given object.
        /// </summary>
        public static PhpTypeInfo GetPhpTypeInfo(this object obj) => obj.GetType().GetPhpTypeInfo();

        /// <summary>
        /// Enumerates self, all base types and all inherited interfaces.
        /// </summary>
        public static IEnumerable<PhpTypeInfo> EnumerateTypeHierarchy(this PhpTypeInfo phptype)
        {
            return EnumerateClassHierarchy(phptype).Concat( // phptype + base types
                phptype.Type.GetInterfaces().Select(GetPhpTypeInfo)); // inherited interfaces
        }

        /// <summary>
        /// Enumerates self and all base types.
        /// </summary>
        static IEnumerable<PhpTypeInfo> EnumerateClassHierarchy(this PhpTypeInfo phptype)
        {
            for (; phptype != null; phptype = phptype.BaseType)
            {
                yield return phptype;
            }
        }
    }

    /// <summary>
    /// Delegate for dynamic object creation.
    /// </summary>
    /// <param name="ctx">Current runtime context. Cannot be <c>null</c>.</param>
    /// <param name="arguments">List of arguments to be passed to called constructor.</param>
    /// <returns>Object instance.</returns>
    public delegate object TObjectCreator(Context ctx, params PhpValue[] arguments);

    #endregion

    #region TypeInfoHolder

    /// <summary>
    /// Helper class holding runtime type information about <typeparamref name="TType"/>.
    /// </summary>
    /// <typeparam name="TType">CLR type.</typeparam>
    internal static class TypeInfoHolder<TType>
    {
        /// <summary>
        /// Associated runtime type information.
        /// </summary>
        public static readonly PhpTypeInfo TypeInfo = new PhpTypeInfo(typeof(TType));
    }

    #endregion
}
