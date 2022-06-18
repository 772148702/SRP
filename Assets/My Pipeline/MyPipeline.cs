using My_Pipeline;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;

public class MyPipeline : RenderPipeline {
	

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
	private LightConfigurator _lightConfigurator = new LightConfigurator();
	
	
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
		
		
		//设置摄像机参数
		context.SetupCameraProperties(camera);
		//对场景进行裁剪
		cullingResults = context.Cull(ref cullingParams);
	
		sortingSetting = new SortingSettings(camera);
		sortingSetting.criteria =SortingCriteria.CommonOpaque;
		drawSetting = new DrawingSettings(new ShaderTagId("SRPDefaultUnlit"), sortingSetting);
		drawSetting.enableInstancing = instancing;
		drawSetting.enableDynamicBatching = dynamicBatching;
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