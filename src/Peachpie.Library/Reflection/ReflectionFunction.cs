﻿using System;
using System.Diagnostics;
using System.Reflection;
using Pchp.Core;
using Pchp.Core.Reflection;

namespace Pchp.Library.Reflection
{
    [PhpType(PhpTypeAttribute.InheritName), PhpExtension(ReflectionUtils.ExtensionName)]
    public class ReflectionFunction : ReflectionFunctionAbstract
    {
        #region Construction

        [PhpFieldsOnlyCtor]
        protected ReflectionFunction() { }

        internal ReflectionFunction(RoutineInfo routine)
        {
            Debug.Assert(routine != null);
            _routine = routine;
        }

        public ReflectionFunction(Context ctx, PhpValue name)
        {
            __construct(ctx, name);
        }

        public void __construct(Context ctx, PhpValue name)
        {
            Debug.Assert(_routine == null, "Subsequent call not allowed.");

            object instance;
            var str = name.ToStringOrNull();
            if (str != null)
            {
                _routine = ctx.GetDeclaredFunction(str);
            }
            else if ((instance = name.AsObject()) != null)
            {
                if (instance is Closure)
                {
                    // _routine = ((Closure)instance).routine; // TODO: handle its $this parameter and use parameters
                    throw new NotImplementedException();
                }
            }

            if (_routine == null)
            {
                throw new ArgumentException();  // TODO: ReflectionException
            }
        }

        #endregion

        public static string export(string name, bool @return = false) { throw new NotImplementedException(); }
        public Closure getClosure(Context ctx) => Operators.BuildClosure(ctx, _routine, null, default(RuntimeTypeHandle), null, PhpArray.Empty, PhpArray.Empty);
        public PhpValue invoke(Context ctx, params PhpValue[] args) => _routine.PhpCallable(ctx, args);
        public PhpValue invokeArgs(Context ctx, PhpArray args) => _routine.PhpCallable(ctx, args.GetValues());
        public bool isDisabled() => false;
        public override string getFileName(Context ctx)
        {
            var methods = _routine.Methods;
            if (methods.Length == 1 && methods[0].IsStatic)
            {
                var scriptattr = methods[0].DeclaringType.GetCustomAttribute<ScriptAttribute>(false);
                if (scriptattr != null)
                {
                    return System.IO.Path.Combine(ctx.RootPath, scriptattr.Path);
                }
            }

            return string.Empty;
        }
    }
}
