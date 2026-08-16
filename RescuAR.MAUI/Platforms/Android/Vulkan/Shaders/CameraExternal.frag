#version 450

layout(set = 0, binding = 0)
uniform sampler2D cameraTexture;

layout(push_constant) uniform CameraUvTransform
{
    vec2 topLeft;
    vec2 topRight;
    vec2 bottomLeft;
    vec2 bottomRight;
} cameraUv;

layout(location = 0) in vec2 inUv;
layout(location = 0) out vec4 outColor;

void main()
{
    vec2 top =
        mix(
            cameraUv.topLeft,
            cameraUv.topRight,
            inUv.x);

    vec2 bottom =
        mix(
            cameraUv.bottomLeft,
            cameraUv.bottomRight,
            inUv.x);

    vec2 transformedUv =
        mix(
            top,
            bottom,
            inUv.y);

    outColor =
        texture(
            cameraTexture,
            transformedUv);
}
