#version 430 core

layout(location = 0) out vec2 vSt;

void main()
{
    vec2 tri = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    gl_Position = vec4(tri * 2.0 - 1.0, 0.0, 1.0);
    vSt = vec2(tri.x, 1.0 - tri.y);
}
