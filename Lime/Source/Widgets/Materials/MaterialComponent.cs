namespace Lime
{
	/// <summary>
	/// This interface must implements every node which can be used as a owner of MaterialComponent.
	/// </summary>
	public interface IMaterialComponentOwner
	{
		/// <summary>
		/// Assign provided material
		/// </summary>
		void AssignMaterial(IMaterial material);
		/// <summary>
		/// Reset material to default
		/// </summary>
		void ResetMaterial();
		ITexture Texture { get; }
		Vector2 UV0 { get; }
		Vector2 UV1 { get; }
	}

	[MutuallyExclusiveDerivedComponents]
	[AllowedComponentOwnerTypes(typeof(IMaterialComponentOwner))]
	public class MaterialComponent : NodeComponent
	{

	}

	/// <summary>
	/// Replace owner material with specified material
	/// </summary>
	public class MaterialComponent<T> : MaterialComponent where T : IMaterial, new()
	{
		protected T CustomMaterial { get; private set; }

		public MaterialComponent()
		{
			CustomMaterial = new T();
		}

		protected override void OnOwnerChanged(Node oldOwner)
		{
			base.OnOwnerChanged(oldOwner);
			if (oldOwner is IMaterialComponentOwner w) {
				w.ResetMaterial();
			}
			if (Owner is IMaterialComponentOwner w1) {
				w1.AssignMaterial(CustomMaterial);
			}
		}
	}
}
