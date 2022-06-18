using UnityEngine;

public class InstancedColor : MonoBehaviour {

	static MaterialPropertyBlock propertyBlock;

	static int colorID = Shader.PropertyToID("_Color");

	[SerializeField]
	Color color = Color.white;

	void Awake () {
		OnValidate();
	}

	void OnValidate () {
		if (propertyBlock == null) {
			propertyBlock = new MaterialPropertyBlock();
		}
		//Shader.SetGlobalColor(colorID, color);
		propertyBlock.SetColor(colorID, color);
		GetComponent<MeshRenderer>().SetPropertyBlock(propertyBlock);
	}
}