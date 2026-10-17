using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.GeneratedCrud.AotFixture;

/// <summary>
/// #128 S4 Step B seam: simulates the response side of a request failing while the response body is
/// being written (a dropped connection is the production shape of this failure). One-shot: arm it
/// and exactly the next request runs with a body stream whose first write throws; everything the
/// handler did — including the unit-of-work commit — has already happened by then.
/// </summary>
public sealed class ResponseWriteFailureGate
{
    private int _armedForNextWrite;

    public void ArmForNextWrite() => Interlocked.Exchange(ref _armedForNextWrite, 1);

    public bool TryConsumeArm() => Interlocked.CompareExchange(ref _armedForNextWrite, 0, 1) == 1;
}

/// <summary>A body stream whose writes always fail, standing in for a broken response connection.</summary>
internal sealed class ThrowingResponseBodyStream : Stream
{
    internal const string FailureMessage = "simulated response write failure (fixture gate)";

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => throw new IOException(FailureMessage);

    public override Task FlushAsync(CancellationToken cancellationToken)
        => Task.FromException(new IOException(FailureMessage));

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => throw new IOException(FailureMessage);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => Task.FromException(new IOException(FailureMessage));

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => ValueTask.FromException(new IOException(FailureMessage));
}
