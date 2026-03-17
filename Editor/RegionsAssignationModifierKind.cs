/*
Copyright (c) 2026 Xavier Arpa López Thomas Peter ('xavierarpa')

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System;

namespace RegionsAssignation.Editor
{
    [Flags]
    internal enum RegionsAssignationModifierKind
    {
        None = 0,
        Static = 1 << 0,
        Abstract = 1 << 1,
        Virtual = 1 << 2,
        Sealed = 1 << 3,
        Extern = 1 << 4,
        Async = 1 << 5,
        Readonly = 1 << 6,
        Volatile = 1 << 7,
        Const = 1 << 8,
        New = 1 << 9,
        Partial = 1 << 10,
        Unsafe = 1 << 11,
        Any = Static | Abstract | Virtual | Sealed | Extern | Async | Readonly | Volatile | Const | New | Partial | Unsafe
    }
}
