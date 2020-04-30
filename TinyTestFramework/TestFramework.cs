using System;
using Unity.Collections.LowLevel.Unsafe;

namespace NUnit.Framework
{
    public class AssertionException : Exception
    {
        public AssertionException(string _ = null) {}
    }

    public class TestAttribute : Attribute
    {
        public TestAttribute() {}

        public virtual string Description
        {
            get => "";
            set {}
        }
    }

    public class ExplicitAttribute : Attribute
    {
        public ExplicitAttribute(string msg) {}
    }

    public class TestFixtureAttribute : Attribute
    {
    }

    public class SetUpAttribute : Attribute
    {
    }

    public class TearDownAttribute : Attribute
    {
    }

    public class IgnoreAttribute : Attribute
    {
        public IgnoreAttribute(string msg)
        {
        }
    }

    public class ValuesAttribute : Attribute
    {
        public ValuesAttribute(params int[] list)
        {
        }

        public ValuesAttribute(params bool[] list)
        {
        }

        // bool true/false
        public ValuesAttribute() {}
    }

    public class RepeatAttribute : Attribute
    {
        public RepeatAttribute(int n)
        {

        }
    }

    public class RangeAttribute : Attribute
    {
        public RangeAttribute(int a, int b) {}
    }


    public delegate void TestDelegate();

    public static class UnitTestRunner
    {
        public static void Run()
        {
            throw new Exception("Should be replaced by code-gen.");
        }
    }
}
