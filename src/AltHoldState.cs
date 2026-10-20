namespace ChengOrbit;

// The intercepted key's down/up events are authoritative. Never infer release
// from GetAsyncKeyState: consuming a down event can leave that state unset.
internal sealed class AltHoldState
{
    public bool IsHeld { get; private set; }
    public bool Cancelled { get; private set; }
    public bool Forwarded { get; private set; }
    public long Id { get; private set; }
    public bool Begin()
    {
        if(IsHeld)return false;
        IsHeld=true;Cancelled=false;Forwarded=false;Id++;return true;
    }
    public void CancelSelection(bool forwardToSystem)
    {
        if(!IsHeld)return;
        Cancelled=true;Forwarded|=forwardToSystem;
    }
    public AltRelease Release()
    {
        var release=new AltRelease(Id,IsHeld&&!Cancelled,IsHeld&&Forwarded);
        IsHeld=false;return release;
    }
    public bool IsCurrent(long id)=>Id==id;
    public void Reset(){IsHeld=false;Cancelled=false;Forwarded=false;Id++;}
}
internal readonly record struct AltRelease(long Id,bool Commit,bool ForwardUp);
