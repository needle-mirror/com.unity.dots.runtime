namespace Unity.Baselib
{
    internal static class BaselibNativeLibrary
    {
#if BASELIB_MANAGED_TESTS
        public const string DllName = "baselib";
#else
        // TODO: pinvoke interface not yet supported/used outside of baselib testing.
        //public const string DllName = "__Internal";
#endif
    }
}
