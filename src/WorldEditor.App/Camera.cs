using System.Numerics;

namespace WorldEditor.App;

internal sealed class Camera
{
    public Vector3 Position = new(64, 55, 155);
    public float Yaw = -90.0f;
    public float Pitch = -28.0f;

    public Vector3 Forward
    {
        get
        {
            var yaw = MathF.PI / 180.0f * Yaw;
            var pitch = MathF.PI / 180.0f * Pitch;
            return Vector3.Normalize(new Vector3(
                MathF.Cos(yaw) * MathF.Cos(pitch),
                MathF.Sin(pitch),
                MathF.Sin(yaw) * MathF.Cos(pitch)));
        }
    }

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Position + Forward, Vector3.UnitY);

    public void Rotate(float deltaX, float deltaY)
    {
        Yaw += deltaX * 0.12f;
        Pitch = Math.Clamp(Pitch - deltaY * 0.12f, -86.0f, 86.0f);
    }

    public void Pan(float deltaX, float deltaY)
    {
        Position -= Right * deltaX * 0.08f;
        Position += Vector3.UnitY * deltaY * 0.08f;
    }
}
