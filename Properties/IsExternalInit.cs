// C# records require this marker type when compiling for .NET Framework.
#if NET48
namespace System.Runtime.CompilerServices
{
	internal static class IsExternalInit
	{
	}
}
#endif
