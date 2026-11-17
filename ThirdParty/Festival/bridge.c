/* Infinigame adapter. Synthesis is serialised by the managed caller. */
#include <stdlib.h>
#include <string.h>
#include "flite_hts_engine.h"
#include "cst_error.h"

#ifdef _WIN32
#include <windows.h>
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

EXPORT float *InfinigameSynthesise(const char *words, const char *directory, int *count)
{
    const char *names[] = {
        "dur.pdf", "tree-dur.inf", "mgc.pdf", "tree-mgc.inf",
        "mgc.win1", "mgc.win2", "mgc.win3", "lf0.pdf", "tree-lf0.inf",
        "lf0.win1", "lf0.win2", "lf0.win3", "gv-mgc.pdf", "tree-gv-mgc.inf",
        "gv-lf0.pdf", "tree-gv-lf0.inf", "gv-switch.inf"
    };
    FILE *files[17] = {0};
    Flite_HTS_Engine voice;
    float *samples = NULL;
    *count = 0;
    if (!words || !*words || strlen(words) > 2000 || !directory) return NULL;
    for (int i = 0; i < 17; i++) {
        char *path = malloc(strlen(directory) + strlen(names[i]) + 2);
        if (!path) goto close_files;
        sprintf(path, "%s/%s", directory, names[i]);
#ifdef _WIN32
        int length = MultiByteToWideChar(CP_UTF8, 0, path, -1, NULL, 0);
        wchar_t *wide = malloc(length * sizeof(wchar_t));
        if (wide) {
            MultiByteToWideChar(CP_UTF8, 0, path, -1, wide, length);
            files[i] = _wfopen(wide, L"rb");
            free(wide);
        }
#else
        files[i] = fopen(path, "rb");
#endif
        free(path);
        if (!files[i]) goto close_files;
    }
    /* Parameters from the original Festival SLT voice, including its 32 kHz rate. */
    Flite_HTS_Engine_initialize(&voice, FALSE, 32000, 160, 0.45, 0, 0, 0, 0.5, FALSE, 1, 1, 1);
    Flite_HTS_Engine_load(&voice, files[0], files[1], files[2], files[3], files + 4, 3,
        files[7], files[8], files + 9, 3, NULL, NULL, NULL, 0,
        files[12], files[13], files[14], files[15], NULL, NULL, files[16]);
    Flite_HTS_Engine_synthesis(&voice, (char *)words, NULL);
    int length = HTS_GStreamSet_get_total_nsample(&voice.engine.gss);
    if (length > 0 && length <= 32000 * 180) {
        samples = malloc(length * sizeof(float));
        if (samples) {
            for (int i = 0; i < length; i++) samples[i] = voice.engine.gss.gspeech[i] / 32768.0f;
            *count = length;
        }
    }
    HTS_Engine_refresh(&voice.engine);
    Flite_HTS_Engine_clear(&voice);
close_files:
    for (int i = 0; i < 17; i++) if (files[i]) fclose(files[i]);
    return samples;
}

EXPORT void InfinigameSpeechFree(float *samples) { free(samples); }
