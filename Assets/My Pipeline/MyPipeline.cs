using My_Pipeline;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;

public class MyPipeline : RenderPipeline {
	
	const int maxVisibleLights = 16;
	const string shadowsHardKeyword = "_SHADOWS_HARD";
	const string shadowsSoftKeyword = "_SHADOWS_SOFT";
	static int visibleLightColorsId = Shader.PropertyToID("_VisibleLightColors");
	static int visibleLightDirectionsOrPositionsId = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
	static int visibleLightAttenuationsId = Shader.PropertyToID("_VisibleLightAttenuations");
	static int visibleLightSpotDirectionsId = Shader.PropertyToID("_VisibleLightSpotDirections");
	static int lightIndicesOffsetAndCountID = Shader.PropertyToID("unity_LightIndicesOffsetAndCount");

	static int shadowMapId = Shader.PropertyToID("_ShadowMap");
	static int worldToShadowMatricesId = Shader.PropertyToID("_WorldToShadowMatrices");
	static int shadowBiasId = Shader.PropertyToID("_ShadowBias");
	static int shadowDataId = Shader.PropertyToID("_ShadowData");
	static int shadowMapSizeId = Shader.PropertyToID("_ShadowMapSize");
	
	Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
	Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];
	Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
	Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];
	RenderTexture shadowMap;
	Vector4[] shadowData = new Vector4[maxVisibleLights];
	Matrix4x4[] worldToShadowMatrices = new Matrix4x4[maxVisibleLights];

	
	Material errorMaterial;
	private CullingResults cullingResults;
	private SortingSettings sortingSetting;
	private DrawingSettings drawSetting ;
	private FilteringSettings filterSetting;

	private bool dynamicBatching = false;
	private bool instancing = false;
	CommandBuffer cameraBuffer = new CommandBuffer {
		name = "Render Camera"
	};
	CommandBuffer shadowBuffer = new CommandBuffer {
		name = "Render Shadows"
	};
	private ShaderTagId _shaderTag = new ShaderTagId("SRPDefaultUnlit");

	int shadowMapSize;
	int shadowTileCount;
	
	public MyPipeline (bool dynamicBatching, bool instancing,int shadowMapSize)
	{
		this.dynamicBatching = dynamicBatching;
		this.instancing = instancing;
		this.shadowMapSize = shadowMapSize;
	}
	
	protected override void Render(ScriptableRenderContext context, Camera[] cameras)
	{
	
		//遍历摄像机，进行渲染
		foreach(var camera in cameras){
			RenderPerCamera(context,camera);
		}

	}


	private void RenderPerCamera(ScriptableRenderContext context, Camera camera)
	{
		if (!camera.TryGetCullingParameters(out var cullingParams))
		{
			return;
		}
// #if UNITY_EDITOR
// 		if (camera.cameraType == CameraType.SceneView) {
// 			ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
// 		}
// #endif
	
		
		//设置摄像机参数
		context.SetupCameraProperties(camera);
		//对场景进行裁剪
		cullingResults = context.Cull(ref cullingParams);
	
		
		if (cullingResults.visibleLights.Length > 0) {
			ConfigureLights();
			if (shadowTileCount > 0) 
			{
				RenderShadows(context);
			}
			else
			{
				cameraBuffer.DisableShaderKeyword(shadowsHardKeyword);
				cameraBuffer.DisableShaderKeyword(shadowsSoftKeyword);
			}
		}
		else {
			cameraBuffer.SetGlobalVector(lightIndicesOffsetAndCountID, Vector4.zero);
			cameraBuffer.DisableShaderKeyword(shadowsHardKeyword);
			cameraBuffer.DisableShaderKeyword(shadowsSoftKeyword);
		}
		
		
		cameraBuffer.BeginSample("Render Camera");
		CameraClearFlags clearFlags = camera.clearFlags;
		cameraBuffer.ClearRenderTarget(
			(clearFlags & CameraClearFlags.Depth) != 0,
			(clearFlags & CameraClearFlags.Color) != 0,
			camera.backgroundColor
		);
		cameraBuffer.SetGlobalVectorArray(
			visibleLightColorsId, visibleLightColors
		);
		cameraBuffer.SetGlobalVectorArray(
			visibleLightDirectionsOrPositionsId, visibleLightDirectionsOrPositions
		);
		cameraBuffer.SetGlobalVectorArray(
			visibleLightAttenuationsId, visibleLightAttenuations
		);
		cameraBuffer.SetGlobalVectorArray(
			visibleLightSpotDirectionsId, visibleLightSpotDirections
		);
		context.ExecuteCommandBuffer(cameraBuffer);
		cameraBuffer.Clear();
		
		
		sortingSetting = new SortingSettings(camera);
		sortingSetting.criteria =SortingCriteria.CommonOpaque;
		drawSetting = new DrawingSettings(new ShaderTagId("SRPDefaultUnlit"), sortingSetting);
		drawSetting.enableInstancing = instancing;
		drawSetting.enableDynamicBatching = dynamicBatching;
		if (cullingResults.visibleLights.Length > 0) {
			drawSetting.perObjectData = drawSetting.perObjectData | PerObjectData.LightData |PerObjectData.LightIndices|PerObjectData.Lightmaps|PerObjectData.LightProbe;
		}
		filterSetting = new FilteringSettings(RenderQueueRange.opaque);
		
		context.DrawRenderers(cullingResults, ref drawSetting, ref filterSetting);
		// context.ExecuteCommandBuffer(cameraBuffer);
		// context.Submit();
		
		context.DrawSkybox(camera);
		
		sortingSetting = new SortingSettings(camera);
		sortingSetting.criteria =SortingCriteria.CommonTransparent;
		filterSetting = new FilteringSettings(RenderQueueRange.transparent);
	
		context.DrawRenderers(cullingResults, ref drawSetting, ref filterSetting);
		
		DrawDefaultPipeline(context,camera);
		cameraBuffer.EndSample("Render Camera");
		context.ExecuteCommandBuffer(cameraBuffer);
		cameraBuffer.Clear();
		context.Submit();
		
		if (shadowMap) {
			RenderTexture.ReleaseTemporary(shadowMap);
			shadowMap = null;
		}
	}

	void ConfigureLights () 
	{
		shadowTileCount = 0;
		for (int i = 0; i < cullingResults.visibleLights.Length; i++) 
		{
			if (i == maxVisibleLights) 
			{
				break;
			}
			VisibleLight light = cullingResults.visibleLights[i];
			visibleLightColors[i] = light.finalColor;
			Vector4 attenuation = Vector4.zero;
			attenuation.w = 1f;
			Vector4 shadow = Vector4.zero;
			if (light.lightType == LightType.Directional) 
			{
				Vector4 v = light.localToWorldMatrix.GetColumn(2);
				v.x = -v.x;
				v.y = -v.y;
				v.z = -v.z;
				visibleLightDirectionsOrPositions[i] = v;
			}
			else
			{
				visibleLightDirectionsOrPositions[i] = light.localToWorldMatrix.GetColumn(3);
				attenuation.x = 1f / Mathf.Max(light.range * light.range, 0.00001f);

				if (light.lightType == LightType.Spot) {
					Vector4 v = light.localToWorldMatrix.GetColumn(2);
					v.x = -v.x;
					v.y = -v.y;
					v.z = -v.z;
					visibleLightSpotDirections[i] = v;

					float outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
					float outerCos = Mathf.Cos(outerRad);
					float outerTan = Mathf.Tan(outerRad);
					float innerCos = Mathf.Cos(Mathf.Atan((46f / 64f) * outerTan));
					float angleRange = Mathf.Max(innerCos - outerCos, 0.001f);
					attenuation.z = 1f / angleRange;
					attenuation.w = -outerCos * attenuation.z;
					Light shadowLight = light.light;
					Bounds shadowBounds;
					if (shadowLight.shadows != LightShadows.None && cullingResults.GetShadowCasterBounds(i, out shadowBounds)) 
					{
						shadowTileCount += 1;
						shadow.x = shadowLight.shadowStrength;
						shadow.y = shadowLight.shadows == LightShadows.Soft ? 1f : 0f;
					}
				}
			}

			visibleLightAttenuations[i] = attenuation;
			shadowData[i] = shadow;
		}

		if (cullingResults.visibleLights.Length > maxVisibleLights) 
		{
			var lightIndices = cullingResults.GetLightIndexMap(Allocator.Temp);
			for (int i = maxVisibleLights; i < cullingResults.visibleLights.Length; i++) {
				lightIndices[i] = -1;
			}
			cullingResults.SetLightIndexMap(lightIndices);
		}
	}

		void RenderShadows (ScriptableRenderContext context) 
		{
		int split;
		if (shadowTileCount <= 1) {
			split = 1;
		}
		else if (shadowTileCount <= 4) {
			split = 2;
		}
		else if (shadowTileCount <= 9) {
			split = 3;
		}
		else {
			split = 4;
		}

		float tileSize = shadowMapSize / split;
		float tileScale = 1f / split;
		Rect tileViewport = new Rect(0f, 0f, tileSize, tileSize);

		shadowMap = RenderTexture.GetTemporary(
			shadowMapSize, shadowMapSize, 16, RenderTextureFormat.Shadowmap
		);
		shadowMap.filterMode = FilterMode.Bilinear;
		shadowMap.wrapMode = TextureWrapMode.Clamp;

		CoreUtils.SetRenderTarget(
			shadowBuffer, shadowMap,
			RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
			ClearFlag.Depth
		);
		shadowBuffer.BeginSample("Render Shadows");
		context.ExecuteCommandBuffer(shadowBuffer);
		shadowBuffer.Clear();

		int tileIndex = 0;
		bool hardShadows = false;
		bool softShadows = false;
		for (int i = 0; i < cullingResults.visibleLights.Length; i++) {
			if (i == maxVisibleLights) {
				break;
			}
			if (shadowData[i].x <= 0f) {
				continue;
			}

			Matrix4x4 viewMatrix, projectionMatrix;
			ShadowSplitData splitData;
			if (!cullingResults.ComputeSpotShadowMatricesAndCullingPrimitives(
				i, out viewMatrix, out projectionMatrix, out splitData
			)) {
				shadowData[i].x = 0f;
				continue;
			}

			float tileOffsetX = tileIndex % split;
			float tileOffsetY = tileIndex / split;
			tileViewport.x = tileOffsetX * tileSize;
			tileViewport.y = tileOffsetY * tileSize;
			if (split > 1) {
				shadowBuffer.SetViewport(tileViewport);
				shadowBuffer.EnableScissorRect(new Rect(
					tileViewport.x + 4f, tileViewport.y + 4f,
					tileSize - 8f, tileSize - 8f
				));
			}
			shadowBuffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
			shadowBuffer.SetGlobalFloat(
				shadowBiasId, cullingResults.visibleLights[i].light.shadowBias
			);
			context.ExecuteCommandBuffer(shadowBuffer);
			shadowBuffer.Clear();

			var shadowSettings = new ShadowDrawingSettings(cullingResults, i);
			context.DrawShadows(ref shadowSettings);

			if (SystemInfo.usesReversedZBuffer) {
				projectionMatrix.m20 = -projectionMatrix.m20;
				projectionMatrix.m21 = -projectionMatrix.m21;
				projectionMatrix.m22 = -projectionMatrix.m22;
				projectionMatrix.m23 = -projectionMatrix.m23;
			}
			var scaleOffset = Matrix4x4.identity;
			scaleOffset.m00 = scaleOffset.m11 = scaleOffset.m22 = 0.5f;
			scaleOffset.m03 = scaleOffset.m13 = scaleOffset.m23 = 0.5f;
			worldToShadowMatrices[i] =
				scaleOffset * (projectionMatrix * viewMatrix);

			if (split > 1) {
				var tileMatrix = Matrix4x4.identity;
				tileMatrix.m00 = tileMatrix.m11 = tileScale;
				tileMatrix.m03 = tileOffsetX * tileScale;
				tileMatrix.m13 = tileOffsetY * tileScale;
				worldToShadowMatrices[i] = tileMatrix * worldToShadowMatrices[i];
			}
			tileIndex += 1;
			if (shadowData[i].y <= 0f) {
				hardShadows = true;
			}
			else {
				softShadows = true;
			}
		}

		if (split > 1) {
			shadowBuffer.DisableScissorRect();
		}
		shadowBuffer.SetGlobalTexture(shadowMapId, shadowMap);
		shadowBuffer.SetGlobalMatrixArray(
			worldToShadowMatricesId, worldToShadowMatrices
		);
		shadowBuffer.SetGlobalVectorArray(shadowDataId, shadowData);
		float invShadowMapSize = 1f / shadowMapSize;
		shadowBuffer.SetGlobalVector(
			shadowMapSizeId, new Vector4(
				invShadowMapSize, invShadowMapSize, shadowMapSize, shadowMapSize
			)
		);
		CoreUtils.SetKeyword(shadowBuffer, shadowsHardKeyword, hardShadows);
		CoreUtils.SetKeyword(shadowBuffer, shadowsSoftKeyword, softShadows);
		shadowBuffer.EndSample("Render Shadows");
		context.ExecuteCommandBuffer(shadowBuffer);
		shadowBuffer.Clear();
	}

	
	[Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
	void DrawDefaultPipeline (ScriptableRenderContext context, Camera camera) {
		if (!camera.TryGetCullingParameters(out var cullingParams))
		{
			return;
		}
		cullingResults = context.Cull(ref cullingParams);
		if (errorMaterial == null) {
			Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
			errorMaterial = new Material(errorShader) {
				hideFlags = HideFlags.HideAndDontSave
			};
		}
		sortingSetting = new SortingSettings(camera);
		sortingSetting.criteria =SortingCriteria.RenderQueue;
		drawSetting = new DrawingSettings(new ShaderTagId("ForwardBase"), sortingSetting);
		
		filterSetting = new FilteringSettings(RenderQueueRange.all);	
		 drawSetting.SetShaderPassName(1, new ShaderTagId("PrepassBase"));
		 drawSetting.SetShaderPassName(2, new ShaderTagId("Always"));
		 drawSetting.SetShaderPassName(3, new ShaderTagId("Vertex"));
		 drawSetting.SetShaderPassName(4, new ShaderTagId("VertexLMRGBM"));
		 drawSetting.SetShaderPassName(5, new ShaderTagId("VertexLM"));
		 drawSetting.overrideMaterialPassIndex=  0;
		 drawSetting.overrideMaterial = errorMaterial;
		 context.DrawRenderers(
			cullingResults, ref drawSetting,ref filterSetting
		);
	}
}