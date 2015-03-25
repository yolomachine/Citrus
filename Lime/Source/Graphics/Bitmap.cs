using System.Collections;
using System.IO;
using System.Collections.Generic;
using System;
using ProtoBuf;

namespace Lime
{
	interface IBitmapImplementation : IDisposable
	{
		int GetWidth();
		int GetHeight();
		void LoadFromStream(Stream stream);
		void SaveToStream(Stream stream);
		IBitmapImplementation Crop(IntRectangle cropArea);
		IBitmapImplementation Rescale(int newWidth, int newHeight);
		bool IsValid();
	}

	[ProtoContract]
	public class Bitmap : IDisposable
	{
		IBitmapImplementation implementation;

		public Vector2 Size { get { return new Vector2(Width, Height); } }
		public int Width { get { return implementation.GetWidth(); } }
		public int Height { get { return implementation.GetHeight(); } }

		[ProtoMember(1)]
		public byte[] AsByteArray
		{
			get { return GetByteArray(); }
			set { LoadFromByteArray(value); }
		}

		public Bitmap()
		{
			implementation = new BitmapImplementation(); // standart avatar bitmap size (84,84)
		}

		private Bitmap(IBitmapImplementation implementation)
		{
			this.implementation = implementation;
		}

		public void LoadFromStream(Stream stream)
		{
			implementation.LoadFromStream(stream);
		}

		public void SaveToStream(Stream stream)
		{
			implementation.SaveToStream(stream);
		}

		public Bitmap Clone()
		{
			return Crop(new IntRectangle(0, 0, Width - 1, Height - 1));
		}

		public Bitmap Rescale(int newWidth, int newHeight)
		{
			var newImplementation = implementation.Rescale(newWidth, newHeight);
			return new Bitmap(newImplementation);
		}

		public Bitmap Crop(IntRectangle cropArea)
		{
			var newImplementation = implementation.Crop(cropArea);
			return new Bitmap(newImplementation);
		}

		public void Dispose()
		{
			implementation.Dispose();
		}

		private void LoadFromByteArray(byte[] data)
		{
				using (var stream = new MemoryStream(data)) {
					LoadFromStream(stream);
				}
		}

		private byte[] GetByteArray()
		{
			if (Width == 0 || Height == 0) {
				return null;
			}
			byte[] result;
			using (var stream = new MemoryStream()) {
				SaveToStream(stream);
				result = stream.ToArray();
			}
			return result;
		}

		 public bool IsValid() {

			return implementation != null && implementation.IsValid();
		}
	}
}