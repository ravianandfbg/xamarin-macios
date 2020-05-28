﻿#if !__WATCHOS__

using System;

using Metal;

using NUnit.Framework;

namespace MonoTouchFixtures.Metal {

	[TestFixture]
	public class MTLStageInRegionIndirectArgumentsTest {

		[SetUp]
		public void SetUp ()
		{
			TestRuntime.AssertXcodeVersion (10, 0);
		}

		[Test]
		public void SizeOfMTLStageInRegionIndirectArgumentsTest ()
		{
			unsafe {
				Assert.AreEqual (sizeof (MTLStageInRegionIndirectArguments), 24); // 24 is the size of the native struct
			}
		}
	}
}

#endif // !__WATCHOS__
