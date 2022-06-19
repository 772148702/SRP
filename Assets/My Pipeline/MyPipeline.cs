using My_Pipeline;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;

public class MyPipeline : RenderPipeline {
	
	const int maxVisibleLights = 16;

	static int visibleLightColorsId =
		Shader.PropertyToID("_VisibleLightColors");
	static int visibleLightDirectionsOrPositionsId =
		Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
	static int visibleLightAttenuationsId =
		Shader.PropertyToID("_VisibleLightAttenuations");
	static int visibleLightSpotDirectionsId =
		Shader.PropertyToID("_VisibleLightSpotDirections");
	static int lightIndicesOffsetAndCountID =
		Shader.PropertyToID("unity_LightIndicesOffsetAndCount");

	Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
	Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];
	Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
	Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];
	
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

	private ShaderTagId _shaderTag = new ShaderTagId("SRPDefaultUnlit");

	
	
	public MyPipeline (bool dynamicBatching, bool instancing)
	{
		this.dynamicBatching = dynamicBatching;
		this.instancing = instancing;
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
		cameraBuffer.BeginSample("Render Camera");
		CameraClearFlags clearFlags = camera.clearFlags;
		cameraBuffer.ClearRenderTarget(
			(clearFlags & CameraClearFlags.Depth) != 0,
			(clearFlags & CameraClearFlags.Color) != 0,
			camera.backgroundColor
		);
		
		//设置摄像机参数
		context.SetupCameraProperties(camera);
		//对场景进行裁剪
		cullingResults = context.Cull(ref cullingParams);
	
		
		if (cullingResults.visibleLights.Length > 0) {
			ConfigureLights();
		}
		else {
			cameraBuffer.SetGlobalVector(
				lightIndicesOffsetAndCountID, Vector4.zero
			);
		}
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
		//提交渲染命令
		context.ExecuteCommandBuffer(cameraBuffer);
		context.Submit();
		cameraBuffer.Clear();
		cameraBuffer.EndSample("Render Camera");
	}

	void ConfigureLights () 
	{
		for (int i = 0; i < cullingResults.visibleLights.Length; i++) {
			if (i == maxVisibleLights) {
				break;
			}
			VisibleLight light = cullingResults.visibleLights[i];
			visibleLightColors[i] = light.finalColor;
			Vector4 attenuation = Vector4.zero;
			attenuation.w = 1f;

			if (light.lightType == LightType.Directional) {
				Vector4 v = light.localToWorldMatrix.GetColumn(2);
				v.x = -v.x;
				v.y = -v.y;
				v.z = -v.z;
				visibleLightDirectionsOrPositions[i] = v;
			}
			else {
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
					float innerCos =
						Mathf.Cos(Mathf.Atan((46f / 64f) * outerTan));
					float angleRange = Mathf.Max(innerCos - outerCos, 0.001f);
					attenuation.z = 1f / angleRange;
					attenuation.w = -outerCos * attenuation.z;
				}
			}

			visibleLightAttenuations[i] = attenuation;
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