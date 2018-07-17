﻿#region Copyright
// --------------------------------------------------------------------------------------------------------------------
// <copyright file="VariableType.cs" company="Ian Horswill">
// Copyright (C) 2018 Ian Horswill
//  
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in the
// Software without restriction, including without limitation the rights to use, copy,
// modify, merge, publish, distribute, sublicense, and/or sell copies of the Software,
// and to permit persons to whom the Software is furnished to do so, subject to the
// following conditions:
//  
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
// PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
#endregion
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace PicoSAT
{
    [DebuggerDisplay("{" + nameof(Name) + "}")]
    public abstract class VariableType
    {
        public readonly string Name;

        public static readonly Dictionary<string, VariableType> Types = new Dictionary<string, VariableType>();

        protected VariableType(string name)
        {
            Name = name;
            Types[Name] = this;
        }

        public static VariableType TypeNamed(string n)
        {
            return Types[n];
        }

        public static bool TypeExists(string n)
        {
            return Types.ContainsKey(n);
        }

        public abstract Variable Instantiate(object name, Problem p, Literal condition = null);

        public override string ToString()
        {
            return Name;
        }
    }
}
