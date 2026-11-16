using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Finds the longest axis of the tracked body in physical Monado root coordinates.</summary>
internal static class BodyRotationAxis
{
    public static Vector3 From(InputFrame frame)
    {
        Vector3 headUp = Vector3.Transform(Vector3.UnitY, frame.Head.Orientation);
        if (frame.Trackers is not { } trackers)
            return headUp;

        Vector3 sum = frame.Head.Position;
        int count = 1;
        foreach (var tracker in trackers)
        {
            if (!tracker.IsUsable)
                continue;
            sum += tracker.Position;
            count++;
        }
        if (count == 1)
            return headUp;

        Vector3 mean = sum / count;
        var covariance = new double[3, 3];
        AddCovariance(covariance, frame.Head.Position - mean);
        foreach (var tracker in trackers)
        {
            if (tracker.IsUsable)
                AddCovariance(covariance, tracker.Position - mean);
        }

        // Jacobi diagonalization handles a dominant axis perpendicular to headUp
        // as well as tied eigenvalues, without a power-iteration starting bias.
        var axes = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int iteration = 0; iteration < 24; iteration++)
        {
            int p = 0, q = 1;
            for (int row = 0; row < 3; row++)
                for (int column = row + 1; column < 3; column++)
                    if (Math.Abs(covariance[row, column]) > Math.Abs(covariance[p, q]))
                        (p, q) = (row, column);
            if (Math.Abs(covariance[p, q]) < 1e-12)
                break;

            double angle = 0.5 * Math.Atan2(2 * covariance[p, q], covariance[q, q] - covariance[p, p]);
            double cosine = Math.Cos(angle), sine = Math.Sin(angle);
            double pp = covariance[p, p], qq = covariance[q, q], pq = covariance[p, q];
            for (int index = 0; index < 3; index++)
            {
                if (index != p && index != q)
                {
                    double ip = covariance[index, p], iq = covariance[index, q];
                    covariance[index, p] = covariance[p, index] = cosine * ip - sine * iq;
                    covariance[index, q] = covariance[q, index] = sine * ip + cosine * iq;
                }
                double axisP = axes[index, p], axisQ = axes[index, q];
                axes[index, p] = cosine * axisP - sine * axisQ;
                axes[index, q] = sine * axisP + cosine * axisQ;
            }
            covariance[p, p] = cosine * cosine * pp - 2 * sine * cosine * pq + sine * sine * qq;
            covariance[q, q] = sine * sine * pp + 2 * sine * cosine * pq + cosine * cosine * qq;
            covariance[p, q] = covariance[q, p] = 0;
        }

        int largest = 0;
        for (int index = 1; index < 3; index++)
            if (covariance[index, index] > covariance[largest, largest])
                largest = index;
        double variance = covariance[largest, largest];
        double second = 0;
        for (int index = 0; index < 3; index++)
            if (index != largest)
                second = Math.Max(second, covariance[index, index]);

        // Require spatial spread and a distinct principal axis (1% eigenvalue gap).
        if (variance / count < 1e-6 || variance - second <= variance * 0.01)
            return headUp;
        return Vector3.Normalize(new((float)axes[0, largest], (float)axes[1, largest], (float)axes[2, largest]));
    }

    private static void AddCovariance(double[,] covariance, Vector3 displacement)
    {
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
                covariance[row, column] += (double)displacement[row] * displacement[column];
    }
}
