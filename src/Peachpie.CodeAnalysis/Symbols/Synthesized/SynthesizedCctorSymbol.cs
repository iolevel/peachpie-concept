﻿using Microsoft.CodeAnalysis;

namespace Pchp.CodeAnalysis.Symbols
{
    /// <summary>
    /// Synthesized static constructor.
    /// </summary>
    internal sealed partial class SynthesizedCctorSymbol : SynthesizedMethodSymbol
    {
        public SynthesizedCctorSymbol(TypeSymbol container)
            :base(container, WellKnownMemberNames.StaticConstructorName, true, false, container.DeclaringCompilation.CoreTypes.Void)
        {
            
        }

        internal override PhpCompilation DeclaringCompilation => ContainingType.DeclaringCompilation;

        public override MethodKind MethodKind => MethodKind.StaticConstructor;

        public override Accessibility DeclaredAccessibility => Accessibility.Private;

        internal override bool HasSpecialName => true;

        public override bool HidesBaseMethodsByName => true;
    }
}
