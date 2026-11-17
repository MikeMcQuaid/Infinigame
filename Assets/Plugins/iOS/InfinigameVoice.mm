#import <Foundation/Foundation.h>
#import <Speech/Speech.h>
#import <AVFoundation/AVFoundation.h>
#import <TargetConditionals.h>
#include <stdlib.h>
#include <string.h>

static AVAudioEngine *engine;
static SFSpeechRecognizer *recogniser;
static SFSpeechAudioBufferRecognitionRequest *request;
static SFSpeechRecognitionTask *task;
static NSString *words = @"";
static int state;
static bool tapped;
static int generation;
static AVSpeechSynthesizer *speaker;

static void StopCapture() {
    [engine stop];
    if (tapped) { [engine.inputNode removeTapOnBus:0]; tapped = false; }
    [request endAudio];
}
extern "C" {
char *InfinigameVoices() {
    NSMutableArray *voices = [NSMutableArray array];
    for (AVSpeechSynthesisVoice *voice in [AVSpeechSynthesisVoice speechVoices]) {
        NSString *name = voice.quality > AVSpeechSynthesisVoiceQualityEnhanced ? [voice.name stringByAppendingString:@" (Premium)"]
            : voice.quality == AVSpeechSynthesisVoiceQualityEnhanced ? [voice.name stringByAppendingString:@" (Enhanced)"] : voice.name;
        [voices addObject:@{@"id": voice.identifier, @"name": name, @"language": voice.language}];
    }
    NSData *json = [NSJSONSerialization dataWithJSONObject:@{@"voices": voices} options:0 error:nil];
    return strdup([[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding].UTF8String);
}
void InfinigameSpeak(const char *text, const char *voice, float strength) {
    if (!speaker) speaker = [[AVSpeechSynthesizer alloc] init];
    [speaker stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
    AVSpeechUtterance *line = [AVSpeechUtterance speechUtteranceWithString:[NSString stringWithUTF8String:text]];
    if (voice && *voice) line.voice = [AVSpeechSynthesisVoice voiceWithIdentifier:[NSString stringWithUTF8String:voice]];
    line.pitchMultiplier = 1.0 - 0.3 * strength;
    line.rate = AVSpeechUtteranceDefaultSpeechRate * 0.92;
    [speaker speakUtterance:line];
}
bool InfinigameSpeaking() { return speaker.isSpeaking; }
void InfinigameSilence() { [speaker stopSpeakingAtBoundary:AVSpeechBoundaryImmediate]; }
void InfinigameVoiceStop() {
    if (state == 1) { generation++; state = -1; return; }
    StopCapture();
    state = 4;
    int session = generation;
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, NSEC_PER_SEC), dispatch_get_main_queue(), ^{
        if (generation == session && state == 4) state = words.length ? 3 : -1;
    });
}
void InfinigameVoiceCancel() {
    generation++;
    StopCapture(); [task cancel]; task = nil; words = @""; state = 0;
}
void InfinigameVoiceStart() {
    InfinigameVoiceCancel();
    int session = generation;
    state = 1;
    [SFSpeechRecognizer requestAuthorization:^(SFSpeechRecognizerAuthorizationStatus status) {
        dispatch_async(dispatch_get_main_queue(), ^{
            if (session != generation) return;
            if (status != SFSpeechRecognizerAuthorizationStatusAuthorized) { state = -1; return; }
            [AVCaptureDevice requestAccessForMediaType:AVMediaTypeAudio completionHandler:^(BOOL granted) {
                dispatch_async(dispatch_get_main_queue(), ^{
                    if (session != generation) return;
                    if (!granted) { state = -1; return; }
                    recogniser = [[SFSpeechRecognizer alloc] initWithLocale:[NSLocale localeWithLocaleIdentifier:@"en-GB"]];
                    if (!recogniser.available || !recogniser.supportsOnDeviceRecognition) { state = -1; return; }
#if TARGET_OS_IPHONE
                    [[AVAudioSession sharedInstance] setCategory:AVAudioSessionCategoryPlayAndRecord mode:AVAudioSessionModeMeasurement options:AVAudioSessionCategoryOptionDefaultToSpeaker error:nil];
                    [[AVAudioSession sharedInstance] setActive:YES error:nil];
#endif
                    engine = [[AVAudioEngine alloc] init];
                    request = [[SFSpeechAudioBufferRecognitionRequest alloc] init];
                    request.requiresOnDeviceRecognition = YES;
                    request.shouldReportPartialResults = YES;
                    AVAudioFormat *format = [engine.inputNode outputFormatForBus:0];
                    if (format.sampleRate <= 0 || format.channelCount == 0) { state = -1; return; }
                    [engine.inputNode installTapOnBus:0 bufferSize:1024 format:format block:^(AVAudioPCMBuffer *buffer, AVAudioTime *when) { [request appendAudioPCMBuffer:buffer]; }];
                    tapped = true;
                    task = [recogniser recognitionTaskWithRequest:request resultHandler:^(SFSpeechRecognitionResult *result, NSError *error) {
                        dispatch_async(dispatch_get_main_queue(), ^{
                            if (session != generation) return;
                            if (result) words = result.bestTranscription.formattedString;
                            if (result.isFinal || error) { StopCapture(); state = words.length ? 3 : -1; }
                        });
                    }];
                    [engine prepare];
                    NSError *error = nil;
                    state = [engine startAndReturnError:&error] ? 2 : -1;
                    if (error) StopCapture();
                });
            }];
        });
    }];
}
int InfinigameVoiceState() { return state; }
char *InfinigameVoiceText() { return strdup(words.UTF8String); }
void InfinigameVoiceFree(char *text) { free(text); }
}
