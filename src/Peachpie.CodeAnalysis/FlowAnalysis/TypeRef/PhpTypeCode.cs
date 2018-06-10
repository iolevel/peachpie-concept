﻿namespace Pchp.CodeAnalysis.FlowAnalysis
{
    /// <summary>
    /// Compatible PHP type codes.
    /// </summary>
    public enum PhpTypeCode
    {
        /// <summary>
        /// An invalid value, <c>void</c>.
        /// </summary>
        Undefined = 0,

        /// <summary>
        /// An invalid value, <c>void</c>.
        /// </summary>
        Void = Undefined,

        /// <summary>
        /// The value is of type boolean.
        /// </summary>
        Boolean,

        /// <summary>
        /// 64-bit integer value.
        /// </summary>
        Long,

        /// <summary>
        /// 64-bit floating point number.
        /// </summary>
        Double,

        /// <summary>
        /// Unicode string value. Two-byte (UTF16) readonly string.
        /// </summary>
        String,

        /// <summary>
        /// Both Unicode and Binary writable string value. Encapsulates two-byte (UTF16), single-byte (binary) string and string builder.
        /// </summary>
        WritableString,

        /// <summary>
        /// A PHP array.
        /// </summary>
        PhpArray,

        /// <summary>
        /// A class type, including <c>NULL</c>, <c>Closure</c> or a generic <c>Object</c>.
        /// </summary>
        Object,

        /// <summary>
        /// A PHP resource.
        /// </summary>
        Resource,

        /// <summary>
        /// Object that might be <c>NULL</c>.
        /// Used in combination with <see cref="Object"/> (?).
        /// </summary>
        Null,
    }
}
