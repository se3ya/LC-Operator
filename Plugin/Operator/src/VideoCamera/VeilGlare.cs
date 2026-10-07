using UnityEngine;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private Material? _veilMat;
    private bool _veilChecked;

    private bool LoadVeilMaterial()
    {
        if (_veilChecked) return _veilMat != null;
        _veilChecked = true;

        Shader? shader = AssetsLoader.LoadOptionalAsset<Shader>("VeilGlare");
        if (shader == null) return false;

        _veilMat = new Material(shader) { name = "VC_VeilGlare" };
        return true;
    }

    private void DrawVeilingGlare()
    {
        RenderTexture? feed = _feedTexture;
        if (feed == null) return;
        float glare = VideoCameraConfig.GetEffect(VideoCameraConfig.VeilingGlare);
        if (glare <= Constants.Visual.GlareSkipBelow) return;
        if (!LoadVeilMaterial() || _veilMat == null) return;

        float fear = _fear;

        _veilMat.SetFloat("_Thresh", Mathf.Lerp(Constants.Visual.GlareThresholdCalm, Constants.Visual.GlareThresholdAfraid, fear));
        _veilMat.SetFloat("_Knee", Constants.Visual.GlareKnee);
        _veilMat.SetFloat("_OverRange", Constants.Visual.GlareOverRange);
        _veilMat.SetFloat("_Halo", Mathf.Lerp(Constants.Visual.GlareHaloCalm, Constants.Visual.GlareHaloAfraid, fear) * glare);
        _veilMat.SetFloat("_Veil", Mathf.Lerp(Constants.Visual.GlareVeilCalm, Constants.Visual.GlareVeilAfraid, fear) * glare);
        _veilMat.SetFloat("_Smear", Mathf.Lerp(Constants.Visual.GlareSmearCalm, Constants.Visual.GlareSmearAfraid, fear) * glare);

        int w = feed.width, h = feed.height;
        int qw = Mathf.Max(2, w / 4), qh = Mathf.Max(2, h / 4);
        int ew = Mathf.Max(2, w / 8), eh = Mathf.Max(2, h / 8);
        int sw = Mathf.Max(2, w / 16), sh = Mathf.Max(2, h / 16);

        RenderTexture quarter = Temp(qw, qh, feed.format);
        RenderTexture quarterB = Temp(qw, qh, feed.format);
        RenderTexture eighth = Temp(ew, eh, feed.format);
        RenderTexture eighthB = Temp(ew, eh, feed.format);
        RenderTexture six = Temp(sw, sh, feed.format);
        RenderTexture sixB = Temp(sw, sh, feed.format);
        RenderTexture smear = Temp(sw, sh, feed.format);
        RenderTexture veil = Temp(8, 8, feed.format);
        RenderTexture sceneCopy = Temp(w, h, feed.format);

        Graphics.Blit(feed, quarter, _veilMat, 0);

        _veilMat.SetVector("_BlurDir", new Vector4(Constants.Visual.GlareNearStep, 0f, 0f, 0f));
        Graphics.Blit(quarter, quarterB, _veilMat, 1);
        _veilMat.SetVector("_BlurDir", new Vector4(0f, Constants.Visual.GlareNearStep, 0f, 0f));
        Graphics.Blit(quarterB, quarter, _veilMat, 1);

        Graphics.Blit(quarter, eighth);
        _veilMat.SetVector("_BlurDir", new Vector4(Constants.Visual.GlareMidStep, 0f, 0f, 0f));
        Graphics.Blit(eighth, eighthB, _veilMat, 1);
        _veilMat.SetVector("_BlurDir", new Vector4(0f, Constants.Visual.GlareMidStep, 0f, 0f));
        Graphics.Blit(eighthB, eighth, _veilMat, 1);

        Graphics.Blit(eighth, six);
        _veilMat.SetVector("_BlurDir", new Vector4(Constants.Visual.GlareFarStep, 0f, 0f, 0f));
        Graphics.Blit(six, sixB, _veilMat, 1);
        _veilMat.SetVector("_BlurDir", new Vector4(0f, Constants.Visual.GlareFarStep, 0f, 0f));
        Graphics.Blit(sixB, six, _veilMat, 1);

        _veilMat.SetVector("_BlurDir", new Vector4(0f, Constants.Visual.GlareSmearStep, 0f, 0f));
        Graphics.Blit(sixB, smear, _veilMat, 1);
        Graphics.Blit(smear, sixB, _veilMat, 1);

        Graphics.Blit(six, veil);
        Graphics.Blit(feed, sceneCopy);

        _veilMat.SetTexture("_GlowTex", quarter);
        _veilMat.SetTexture("_GlowFar", six);
        _veilMat.SetTexture("_VeilTex", veil);
        _veilMat.SetTexture("_SmearTex", sixB);
        Graphics.Blit(sceneCopy, feed, _veilMat, 2);

        RenderTexture.ReleaseTemporary(quarter);
        RenderTexture.ReleaseTemporary(quarterB);
        RenderTexture.ReleaseTemporary(eighth);
        RenderTexture.ReleaseTemporary(eighthB);
        RenderTexture.ReleaseTemporary(six);
        RenderTexture.ReleaseTemporary(sixB);
        RenderTexture.ReleaseTemporary(smear);
        RenderTexture.ReleaseTemporary(veil);
        RenderTexture.ReleaseTemporary(sceneCopy);
    }

    private static RenderTexture Temp(int width, int height, RenderTextureFormat format)
    {
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, format);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Clamp;
        return rt;
    }
}
