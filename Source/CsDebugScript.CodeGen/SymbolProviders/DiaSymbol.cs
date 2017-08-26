﻿using CsDebugScript.Engine;
using Dia2Lib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CsDebugScript.CodeGen.SymbolProviders
{
    /// <summary>
    /// Class represents symbol during debugging.
    /// </summary>
    public class DiaSymbol : Symbol
    {
        /// <summary>
        /// The DIA symbol
        /// </summary>
        internal IDiaSymbol symbol;

        /// <summary>
        /// Initializes a new instance of the <see cref="DiaSymbol"/> class.
        /// </summary>
        /// <param name="module">The module.</param>
        /// <param name="symbol">The DIA symbol.</param>
        public DiaSymbol(DiaModule module, IDiaSymbol symbol)
            : base(module)
        {
            SymTagEnum symTag = (SymTagEnum)symbol.symTag;

            this.symbol = symbol;
            Tag = ConvertToCodeTypeTag(symTag);
            BasicType = (BasicType)symbol.baseType;
            Id = symbol.symIndexId;
            if (symTag != SymTagEnum.SymTagExe)
            {
                Name = TypeToString.GetTypeString(symbol);
                Name = Name.Replace("<enum ", "<").Replace(",enum ", ",");
            }
            else
            {
                Name = "";
            }

            Offset = symbol.offset;

            ulong size = symbol.length;

            if (size > int.MaxValue)
            {
                throw new ArgumentException("Symbol size is unexpected");
            }
            Size = (int)size;
        }

        /// <summary>
        /// Gets the DIA module.
        /// </summary>
        internal DiaModule DiaModule
        {
            get
            {
                return (DiaModule)Module;
            }
        }

        /// <summary>
        /// Gets the enumeration values.
        /// </summary>
        protected override IEnumerable<Tuple<string, string>> GetEnumValues()
        {
            if (Tag == CodeTypeTag.Enum)
            {
                foreach (var enumValue in symbol.GetChildren())
                {
                    yield return Tuple.Create(enumValue.name, enumValue.value.ToString());
                }
            }
        }

        /// <summary>
        /// Casts as symbol field.
        /// </summary>
        public override SymbolField CastAsSymbolField()
        {
            return new DiaSymbolField(this, symbol);
        }

        /// <summary>
        /// Initializes the cache.
        /// </summary>
        public override void InitializeCache()
        {
            if (Tag != CodeTypeTag.ModuleGlobals)
            {
                var elementType = this.ElementType;
            }
        }

        /// <summary>
        /// Determines whether symbol has virtual table of functions.
        /// </summary>
        public override bool HasVTable()
        {
            if (symbol.GetChildren(SymTagEnum.SymTagVTable).Any())
            {
                return true;
            }
            foreach (Symbol baseClass in BaseClasses)
            {
                if (baseClass.Offset == 0 && baseClass.HasVTable())
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets user type fields.
        /// </summary>
        protected override IEnumerable<SymbolField> GetFields()
        {
            return symbol.GetChildren(SymTagEnum.SymTagData).Select(s => new DiaSymbolField(this, s)).Where(f => f.Type != null).Cast<SymbolField>();
        }

        /// <summary>
        /// Gets user type base classes.
        /// </summary>
        protected override IEnumerable<Symbol> GetBaseClasses()
        {
            return symbol.GetChildren(SymTagEnum.SymTagBaseClass).Select(s => DiaModule.GetSymbol(s)).Cast<Symbol>();
        }

        /// <summary>
        /// Gets the element type (if symbol is array or pointer).
        /// </summary>
        protected override Symbol GetElementType()
        {
            if (Tag == CodeTypeTag.Pointer || Tag == CodeTypeTag.Array)
            {
                IDiaSymbol type = symbol.type;

                if (type != null)
                {
                    Symbol result = DiaModule.GetSymbol(type);

                    if (Tag == CodeTypeTag.Pointer)
                    {
                        result.PointerType = this;
                    }
                    return result;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the type of the pointer.
        /// </summary>
        protected override Symbol GetPointerType()
        {
            Symbol result = DiaModule.GetSymbol(symbol.objectPointerType);

            result.ElementType = this;
            return result;
        }

        /// <summary>
        /// Converts <see cref="SymTagEnum"/> to <see cref="CodeTypeTag"/>.
        /// </summary>
        public static CodeTypeTag ConvertToCodeTypeTag(SymTagEnum tag)
        {
            switch (tag)
            {
                case SymTagEnum.SymTagArrayType:
                    return CodeTypeTag.Array;
                case SymTagEnum.SymTagBaseType:
                    return CodeTypeTag.BuiltinType;
                case SymTagEnum.SymTagUDT:
                    // TODO: What about Structure/Union?
                    return CodeTypeTag.Class;
                case SymTagEnum.SymTagEnum:
                    return CodeTypeTag.Enum;
                case SymTagEnum.SymTagFunctionType:
                    return CodeTypeTag.Function;
                case SymTagEnum.SymTagPointerType:
                    return CodeTypeTag.Pointer;
                case SymTagEnum.SymTagBaseClass:
                    return CodeTypeTag.BaseClass;
                case SymTagEnum.SymTagExe:
                    return CodeTypeTag.ModuleGlobals;
                default:
                    return CodeTypeTag.Unsupported;
            }
        }
    }
}
