package com.workguidance.media;

import android.app.Activity;
import android.content.ContentResolver;
import android.content.ContentValues;
import android.media.MediaScannerConnection;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.provider.MediaStore;
import com.unity3d.player.UnityPlayer;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.OutputStream;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

/**
 * グラスのアルバム（com.rayneo.gallery）は DCIM/Camera を見る。
 * Pictures では表示されない。
 */
public class GallerySaver {
    public static String saveJpegFile(String srcPath, String unusedDisplayName) {
        Activity activity = UnityPlayer.currentActivity;
        if (activity == null || srcPath == null) {
            return "no-activity";
        }

        File src = new File(srcPath);
        if (!src.exists()) {
            return "missing";
        }

        String stamp = new SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(new Date());
        String displayName = "IMG_" + stamp + "_FixLens.jpg";

        try {
            ContentValues values = new ContentValues();
            values.put(MediaStore.Images.Media.DISPLAY_NAME, displayName);
            values.put(MediaStore.Images.Media.MIME_TYPE, "image/jpeg");
            values.put(MediaStore.Images.Media.DATE_TAKEN, System.currentTimeMillis());
            if (Build.VERSION.SDK_INT >= 29) {
                values.put(MediaStore.Images.Media.RELATIVE_PATH, Environment.DIRECTORY_DCIM + "/Camera");
                values.put(MediaStore.Images.Media.IS_PENDING, 1);
            }

            ContentResolver resolver = activity.getContentResolver();
            Uri uri = resolver.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, values);
            if (uri != null) {
                OutputStream out = resolver.openOutputStream(uri);
                copy(src, out);
                if (Build.VERSION.SDK_INT >= 29) {
                    values.clear();
                    values.put(MediaStore.Images.Media.IS_PENDING, 0);
                    resolver.update(uri, values, null, null);
                }
                return uri.toString();
            }
        } catch (Exception ignored) {
        }

        try {
            File cameraDir = new File(Environment.getExternalStoragePublicDirectory(
                    Environment.DIRECTORY_DCIM), "Camera");
            if (!cameraDir.exists()) {
                cameraDir.mkdirs();
            }
            File dest = new File(cameraDir, displayName);
            FileOutputStream out = new FileOutputStream(dest);
            copy(src, out);
            MediaScannerConnection.scanFile(
                    activity,
                    new String[] { dest.getAbsolutePath() },
                    new String[] { "image/jpeg" },
                    null);
            return dest.getAbsolutePath();
        } catch (Exception exception) {
            return "error:" + exception.getMessage();
        }
    }

    private static void copy(File src, OutputStream out) throws Exception {
        FileInputStream in = new FileInputStream(src);
        byte[] buffer = new byte[8192];
        int n;
        while ((n = in.read(buffer)) > 0) {
            out.write(buffer, 0, n);
        }
        in.close();
        out.close();
    }
}
