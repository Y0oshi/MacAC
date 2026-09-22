#version 430 core
in vec3 vTintIn;
out vec4 FragTint;

void main()
{
    FragTint = vec4(vTintIn, 1.0);
}
