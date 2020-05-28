using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

using AppKit;
using ObjCRuntime;
using Foundation;

namespace Xamarin.Mac.Tests
{
	public class NSWindowControllerTests
	{
		[Test]
		public void NSWindowController_ShowWindowTest ()
		{
			NSWindowController c = new NSWindowController ();
			c.ShowWindow (null);
		}
	}
}
