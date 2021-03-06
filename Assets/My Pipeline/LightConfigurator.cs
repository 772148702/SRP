
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace My_Pipeline
{
    public class LightConfigurator
    {
        private static int CompareLightRenderMode(LightRenderMode m1,LightRenderMode m2)
        {
            if(m1 == m2){
                return 0;
            }
            if(m1 == LightRenderMode.ForcePixel){
                return -1;
            }
            if(m2 == LightRenderMode.ForcePixel){
                return 1;
            }
            if(m1 == LightRenderMode.Auto){
                return -1;
            }
            if(m2 == LightRenderMode.Auto){
                return 1;
            }
            return 0;
        }
        
        private static int CompareLight(Light l1,Light l2)
        {
            if(l1.renderMode == l2.renderMode){
                return (int)Mathf.Sign(l2.intensity - l1.intensity);
            }
            var ret = CompareLightRenderMode(l1.renderMode,l2.renderMode);
            if(ret == 0){
                ret = (int)Mathf.Sign(l2.intensity - l1.intensity);
            }
            return ret;
        }
        
        private static int GetMainLightIndex(NativeArray<VisibleLight> lights)
        {
            Light mainLight = null;
            var mainLightIndex = -1;
            var index = 0;
            foreach(var light in lights){
                if(light.lightType == LightType.Directional){
                    var lightComp = light.light;
                    if(lightComp.renderMode == LightRenderMode.ForceVertex){
                        continue;
                    }
                    if(!mainLight){
                        mainLight = lightComp;
                        mainLightIndex = index;
                    }else{
                        if(CompareLight(mainLight,lightComp) > 0){
                            mainLight = lightComp;
                            mainLightIndex = index;
                        }
                    }
                }
                index ++;
            }
            return mainLightIndex;
        }
        
        public void SetupShaderLightingParams(ScriptableRenderContext context, ref CullingResults cullingResults)
        {
            var visibleLights = cullingResults.visibleLights;
            var mainLightIndex = GetMainLightIndex(visibleLights);
            if(mainLightIndex >= 0)
            {
                var mainLight = visibleLights[mainLightIndex];
                var forward = - (Vector4)mainLight.light.gameObject.transform.forward; 
                Shader.SetGlobalVector(ShaderProperties.MainLightDirection,forward);
                Shader.SetGlobalColor(ShaderProperties.MainLightColor,mainLight.finalColor);
            }else{
                Shader.SetGlobalColor(ShaderProperties.MainLightColor,new Color(0,0,0,0));
            }
            Shader.SetGlobalColor(ShaderProperties.AmbientColor,RenderSettings.ambientLight);
        }
        
        public class ShaderProperties{
            public static int MainLightDirection = Shader.PropertyToID("_XMainLightDirection");
            public static int MainLightColor = Shader.PropertyToID("_XMainLightColor");
            public static int AmbientColor = Shader.PropertyToID("_XAmbientColor");
        }
        
    }
}