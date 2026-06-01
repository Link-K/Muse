using Avalonia.Input;
using Avalonia.Platform;

namespace Muse.Tests
{
	public class TestCursorFactory : ICursorFactory
	{
		public ICursorImpl GetCursor(StandardCursorType cursorType)
		{
			return new TestCursorImpl();
		}

		public ICursorImpl CreateCursor(IBitmapImpl cursor, PixelPoint hotSpot)
		{
			return new TestCursorImpl();
		}

		private class TestCursorImpl : ICursorImpl
		{
			public void Dispose() { }
		}
	}
}
