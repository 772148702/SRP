using My_Pipeline;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;

public class MyPipeline : RenderPipeline {
	

	Material errorMaterial;

	CommandBuffer cameraBuffer = new CommandBuffer {
		name = "Render Camera"
	};

	private ShaderTagId _shaderTag = new ShaderTagId("SRPDefaultUnlit");
	private LightConfigurator _lightConfigurator = new LightConfigurator();
	
	private DrawingSettings CreateDrawSettings(Camera camera,SortingCriteria sortingCriteria){
		var sortingSetting = new SortingSettings(camera);
		sortingSetting.criteria =sortingCriteria;
		var drawSetting = new DrawingSettings(_shaderTag, sortingSetting);
		return drawSetting;
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
		if (errorMaterial == null)
		{
			Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
			errorMaterial = new Material(errorShader)
			{
				hideFlags = HideFlags.HideAndDontSave
			};
		}

		if (!camera.TryGetCullingParameters(out var cullingParams))
		{
			return;
		}
#if UNITY_EDITOR
		if (camera.cameraType == CameraType.SceneView) {
			ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
		}
#endif
		cameraBuffer.BeginSample("Render Camera");
		CameraClearFlags clearFlags = camera.clearFlags;
		cameraBuffer.ClearRenderTarget(
			(clearFlags & CameraClearFlags.Depth) != 0,
			(clearFlags & CameraClearFlags.Color) != 0,
			camera.backgroundColor
		);
		
		//cameraBuffer.ClearRenderTarget(true, false, Color.clear);
		
		//设置摄像机参数
		context.SetupCameraProperties(camera);
		//对场景进行裁剪
		var cullingResults = context.Cull(ref cullingParams);
	
		var sortingSetting = new SortingSettings(camera);
		sortingSetting.criteria =SortingCriteria.CommonOpaque;
		var drawSetting = new DrawingSettings(new ShaderTagId("SRPDefaultUnlit"), sortingSetting);
		var filterSetting = new FilteringSettings(RenderQueueRange.opaque);
		
		context.DrawRenderers(cullingResults, ref drawSetting, ref filterSetting);
		// context.ExecuteCommandBuffer(cameraBuffer);
		// context.Submit();
		
		context.DrawSkybox(camera);
		
		sortingSetting = new SortingSettings(camera);
		sortingSetting.criteria =SortingCriteria.CommonTransparent;
		drawSetting = new DrawingSettings(new ShaderTagId("SRPDefaultUnlit"), sortingSetting);
		filterSetting = new FilteringSettings(RenderQueueRange.transparent);
	
		context.DrawRenderers(cullingResults, ref drawSetting, ref filterSetting);
		
		DrawDefaultPipeline(context,camera);
		//提交渲染命令
		context.ExecuteCommandBuffer(cameraBuffer);
		context.Submit();
		cameraBuffer.Clear();
		cameraBuffer.EndSample("Render Camera");
	}

	[Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
	void DrawDefaultPipeline (ScriptableRenderContext context, Camera camera) {
		if (!camera.TryGetCullingParameters(out var cullingParams))
		{
			return;
		}
		var cullingResults = context.Cull(ref cullingParams);
		if (errorMaterial == null) {
			Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
			errorMaterial = new Material(errorShader) {
				hideFlags = HideFlags.HideAndDontSave
			};
		}
		var sortingSetting = new SortingSettings(camera);
		sortingSetting.criteria =SortingCriteria.RenderQueue;
		var drawSettings = new DrawingSettings(new ShaderTagId("ForwardBase"), sortingSetting);
		
		var filterSettings = new FilteringSettings(RenderQueueRange.all);	
		drawSettings.SetShaderPassName(1, new ShaderTagId("PrepassBase"));
		drawSettings.SetShaderPassName(2, new ShaderTagId("Always"));
		drawSettings.SetShaderPassName(3, new ShaderTagId("Vertex"));
		drawSettings.SetShaderPassName(4, new ShaderTagId("VertexLMRGBM"));
		drawSettings.SetShaderPassName(5, new ShaderTagId("VertexLM"));
		drawSettings.overrideMaterialPassIndex=  0;
		drawSettings.overrideMaterial = errorMaterial;
		
		context.DrawRenderers(
			cullingResults, ref drawSettings,ref filterSettings
		);
	}
}