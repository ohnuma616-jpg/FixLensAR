package com.workguidance.speech;

import android.app.Activity;
import android.media.AudioFormat;
import android.media.AudioRecord;
import android.media.MediaRecorder;
import android.os.Handler;
import android.os.Looper;
import com.unity3d.player.UnityPlayer;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;
import org.json.JSONObject;
import org.vosk.LibVosk;
import org.vosk.LogLevel;
import org.vosk.Model;
import org.vosk.Recognizer;

/**
 * グラスの RecognitionService はウェイクワード専用なので、Vosk で端末内キーワード認識する。
 */
public class KeywordSpeechPlugin {
    private static final String MODEL_ZIP = "vosk-model-small-ja-0.22.zip";
    private static final String MODEL_DIR = "vosk-model-small-ja-0.22";
    private static final String GRAMMAR =
            "[\"次へ\",\"つぎへ\",\"次 へ\",\"つぎ へ\",\"次\",\"つぎ\",\"進む\",\"すすむ\",\"戻る\",\"もどる\",\"もう一度\",\"もういちど\",\"原点\",\"ここ\",\"記録\",\"最初から\",\"さいしょから\",\"はじめから\",\"最初\",\"さいしょ\",\"はじめ\",\"続きから\",\"つづきから\",\"続き\",\"つづき\",\"再開\",\"さいかい\",\"[unk]\"]";
    private static final int SAMPLE_RATE = 16000;

    private static KeywordSpeechPlugin instance;

    private final Handler main = new Handler(Looper.getMainLooper());
    private String gameObjectName = "";
    private String methodName = "";
    private volatile boolean running;
    private Thread worker;
    private AudioRecord recorder;
    private Model model;
    private Recognizer recognizer;

    public static void start(final String gameObjectName, final String methodName) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            return;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (instance == null) {
                    instance = new KeywordSpeechPlugin();
                }
                instance.gameObjectName = gameObjectName;
                instance.methodName = methodName;
                instance.running = true;
                instance.sendStatus("loading");
                instance.startWorker(activity);
            }
        });
    }

    public static void stop() {
        if (instance == null) {
            return;
        }
        instance.running = false;
        instance.stopCapture();
    }

    private void startWorker(final Activity activity) {
        if (worker != null && worker.isAlive()) {
            return;
        }
        worker = new Thread(new Runnable() {
            @Override
            public void run() {
                try {
                    LibVosk.setLogLevel(LogLevel.WARNINGS);
                    File modelDir = unpackModel(activity);
                    if (model == null) {
                        model = new Model(modelDir.getAbsolutePath());
                    }
                    if (recognizer == null) {
                        recognizer = new Recognizer(model, (float) SAMPLE_RATE, GRAMMAR);
                    }
                    sendStatus("listening");
                    captureLoop();
                } catch (Exception exception) {
                    sendStatus("unavailable");
                    sendStatus("error:" + exception.getMessage());
                }
            }
        }, "WorkGuidanceVosk");
        worker.start();
    }

    private void captureLoop() {
        int min = AudioRecord.getMinBufferSize(
                SAMPLE_RATE,
                AudioFormat.CHANNEL_IN_MONO,
                AudioFormat.ENCODING_PCM_16BIT);
        if (min <= 0) {
            min = SAMPLE_RATE / 5;
        }
        int bufferSize = Math.max(min, SAMPLE_RATE / 5);

        recorder = new AudioRecord(
                MediaRecorder.AudioSource.MIC,
                SAMPLE_RATE,
                AudioFormat.CHANNEL_IN_MONO,
                AudioFormat.ENCODING_PCM_16BIT,
                bufferSize);
        if (recorder.getState() != AudioRecord.STATE_INITIALIZED) {
            sendStatus("error:mic");
            return;
        }

        short[] buffer = new short[bufferSize / 2];
        recorder.startRecording();
        while (running) {
            int read = recorder.read(buffer, 0, buffer.length);
            if (read <= 0 || recognizer == null) {
                continue;
            }
            try {
                if (recognizer.acceptWaveForm(buffer, read)) {
                    emitJson(recognizer.getResult());
                } else {
                    emitJson(recognizer.getPartialResult());
                }
            } catch (Exception ignored) {
            }
        }
        stopCapture();
    }

    private void stopCapture() {
        AudioRecord current = recorder;
        recorder = null;
        if (current != null) {
            try {
                current.stop();
            } catch (Exception ignored) {
            }
            try {
                current.release();
            } catch (Exception ignored) {
            }
        }
    }

    private File unpackModel(Activity activity) throws Exception {
        File dest = new File(activity.getFilesDir(), MODEL_DIR);
        File marker = new File(dest, "conf/model.conf");
        if (marker.exists()) {
            return dest;
        }

        if (dest.exists()) {
            deleteRecursive(dest);
        }

        InputStream zipStream = activity.getAssets().open(MODEL_ZIP);
        ZipInputStream zip = new ZipInputStream(zipStream);
        byte[] chunk = new byte[8192];
        ZipEntry entry;
        File root = activity.getFilesDir();
        while ((entry = zip.getNextEntry()) != null) {
            File outFile = new File(root, entry.getName());
            String canonicalRoot = root.getCanonicalPath();
            String canonicalOut = outFile.getCanonicalPath();
            if (!canonicalOut.startsWith(canonicalRoot + File.separator) && !canonicalOut.equals(canonicalRoot)) {
                continue;
            }
            if (entry.isDirectory()) {
                outFile.mkdirs();
            } else {
                File parent = outFile.getParentFile();
                if (parent != null) {
                    parent.mkdirs();
                }
                FileOutputStream out = new FileOutputStream(outFile);
                int n;
                while ((n = zip.read(chunk)) > 0) {
                    out.write(chunk, 0, n);
                }
                out.close();
            }
            zip.closeEntry();
        }
        zip.close();
        zipStream.close();

        if (!marker.exists()) {
            throw new IllegalStateException("model unpack failed");
        }
        return dest;
    }

    private static void deleteRecursive(File file) {
        File[] children = file.listFiles();
        if (children != null) {
            for (int i = 0; i < children.length; i++) {
                deleteRecursive(children[i]);
            }
        }
        file.delete();
    }

    private void emitJson(String json) {
        if (json == null || json.length() == 0) {
            return;
        }
        try {
            JSONObject object = new JSONObject(json);
            String text = object.optString("text", "");
            if (text.length() == 0) {
                text = object.optString("partial", "");
            }
            text = text.replace("[unk]", "").trim();
            if (text.length() == 0) {
                return;
            }
            UnityPlayer.UnitySendMessage(gameObjectName, methodName, text);
        } catch (Exception ignored) {
        }
    }

    private void sendStatus(final String status) {
        main.post(new Runnable() {
            @Override
            public void run() {
                if (gameObjectName == null || methodName == null || gameObjectName.length() == 0) {
                    return;
                }
                UnityPlayer.UnitySendMessage(gameObjectName, methodName, "STATUS:" + status);
            }
        });
    }
}
