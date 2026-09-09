#include <jni.h>

#include <android/hardware_buffer.h>
#include <android/hardware_buffer_jni.h>
#include <android/log.h>

/*
 * Some NDK/CMake combinations do not expose this declaration from
 * hardware_buffer_jni.h even though the function is available from API 26.
 */
extern "C" AHardwareBuffer*
AHardwareBuffer_fromHardwareBuffer(
    JNIEnv* env,
    jobject hardwareBufferObj);

#define LOG_TAG "RescuAR-NativeBridge"

#define LOGE(...) \
    __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)

extern "C"
__attribute__((visibility("default")))
AHardwareBuffer* rescuar_from_java_hardware_buffer(
    JNIEnv* env,
    jobject javaHardwareBuffer)
{
    if (env == nullptr)
    {
        LOGE("JNIEnv is null.");
        return nullptr;
    }

    if (javaHardwareBuffer == nullptr)
    {
        LOGE("Java HardwareBuffer object is null.");
        return nullptr;
    }

    AHardwareBuffer* nativeBuffer =
        AHardwareBuffer_fromHardwareBuffer(
            env,
            javaHardwareBuffer);

    if (nativeBuffer == nullptr)
    {
        LOGE(
            "AHardwareBuffer_fromHardwareBuffer returned null.");

        return nullptr;
    }

    /*
     * AHardwareBuffer_fromHardwareBuffer does not acquire an
     * additional reference. Acquire one while Vulkan imports it.
     */
    AHardwareBuffer_acquire(
        nativeBuffer);

    return nativeBuffer;
}

extern "C"
__attribute__((visibility("default")))
void rescuar_release_hardware_buffer(
    AHardwareBuffer* nativeBuffer)
{
    if (nativeBuffer == nullptr)
    {
        return;
    }

    AHardwareBuffer_release(
        nativeBuffer);
}
