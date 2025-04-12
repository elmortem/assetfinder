using System;

namespace AssetScout.Samples
{
	[Serializable]
	public class DoubleTypeProvider
	{
		public MyType MyType;
	}

	[Serializable]
	public class MyType
	{
		public string Value;
	}
}