using System;
using Unity.Collections.LowLevel.Unsafe;

namespace NUnit.Framework.Interfaces
{
    public interface ITestBuilder
    {
    }
}

namespace NUnit.Framework.Internal
{
}

namespace NUnit.Framework.Internal.Builders
{

}

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

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class TestFixtureAttribute : Attribute
    {
    }

    public class SetUpAttribute : Attribute
    {
    }

    public class TearDownAttribute : Attribute
    {
    }

    public class OneTimeSetUpAttribute : Attribute 
    {
    }
    
    public class OneTimeTearDownAttribute : Attribute
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
        public ValuesAttribute(params object[] list)
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

    public class TestCaseAttribute : TestAttribute
    {

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
