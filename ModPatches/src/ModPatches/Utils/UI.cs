using UnityEngine;

namespace Unnamed42.ModPatches.Utils;

public static class UIUtils
{
    public static Matrix4x4? GetScalingMatrix()
    {
        int width = Screen.width, height = Screen.height;
        // 只针对大于1080P的屏幕做缩放
        if (width <= 1920 || height <= 1080)
            return null;
        var x = width / 1920f;
        var y = height / 1080f;
        return Matrix4x4.TRS(new Vector3(0, 0, 0), Quaternion.identity, new Vector3(x, y, 1));
    }
}
