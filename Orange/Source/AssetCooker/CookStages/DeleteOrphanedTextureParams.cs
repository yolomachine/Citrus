using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Orange
{
	class DeleteOrphanedTextureParams: CookStage
	{
		public override IEnumerable<string> ImportedExtensions { get { yield break; } }
		public override IEnumerable<string> BundleExtensions { get { yield return textureParamsExtension; } }

		private readonly string textureParamsExtension = ".texture";

		public override void Action()
		{
			foreach (var path in AssetCooker.AssetBundle.EnumerateFiles().ToList()) {
				if (path.EndsWith(textureParamsExtension, StringComparison.OrdinalIgnoreCase)) {
					var origImageFile = Path.ChangeExtension(path, AssetCooker.GetPlatformTextureExtension());
					if (!AssetCooker.AssetBundle.FileExists(origImageFile)) {
						AssetCooker.DeleteFileFromBundle(path);
					}
				}
			}
		}
	}
}
