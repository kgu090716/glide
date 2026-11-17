using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ThreeFingerWindowDrag
{
    internal static class GestureFeatureChecks
    {
        private const int ConfirmMs = GestureEngine.ThreeFingerConfirmationMs;
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        private static void Require(bool value, string error) { if (!value) throw new Exception(error); }
        private static ContactFrame Triangle(double scale = 1, double dx = 0, double dy = 0)
        {
            var frame = new ContactFrame { Device = new IntPtr(1) };
            double[] x = { -10, 10, 0 }, y = { -6, -6, 12 };
            for (int i=0; i<3; i++) frame.Contacts.Add(new Contact { Id=i, X=20+x[i]*scale+dx, Y=20+y[i]*scale+dy, Tip=true, Confident=true });
            return frame;
        }
        internal static void PinchExpandsAndContracts()
        {
            var mover = new FakeMover(); var engine = new GestureEngine(mover);
            engine.Process(Triangle(),0,false); engine.Process(Triangle(1.2),ConfirmMs,false);
            Require(engine.Resizing && mover.Resizes == 1 && Math.Abs(mover.Scale-1.2)<0.000001 && mover.Moves == 0, "spread was not resize");
            engine.Process(Triangle(0.8),ConfirmMs + 10,false);
            Require(engine.Resizing && Math.Abs(mover.Scale-0.8)<0.000001 && mover.Moves == 0, "contraction was not resize");
            Require(mover.Prepares == 1 && mover.Captures == 1,"resize reselected its target");
        }
        internal static void PinchCanBeDisabled()
        {
            var mover=new FakeMover(); var engine=new GestureEngine(mover) { PinchResizeEnabled=false };
            engine.Process(Triangle(),0,false); engine.Process(Triangle(1.2),ConfirmMs,false);
            Require(mover.Resizes==0 && mover.Captures==0,"disabled pinch resized");
            engine.Process(Triangle(1.2,1),ConfirmMs + 10,false);
            Require(mover.Moves>0 && !engine.Resizing,"disabled pinch prevented normal drag");
            mover=new FakeMover { SupportsResize=false }; engine=new GestureEngine(mover);
            engine.Process(Triangle(),0,false); engine.Process(Triangle(1.2),ConfirmMs,false);
            Require(mover.Resizes==0,"fixed-size target resized");
        }
        internal static void ModeLockAndJitter()
        {
            var mover=new FakeMover(); var engine=new GestureEngine(mover);
            engine.Process(Triangle(),0,false); engine.Process(Triangle(1.01,0.1),ConfirmMs,false);
            Require(mover.Captures==0,"small jitter activated gesture");
            engine.Process(Triangle(1,1),ConfirmMs + 10,false);
            Require(mover.Moves>0 && !engine.Resizing,"translation classified as pinch");
            engine.Process(Triangle(1.3,2),ConfirmMs + 20,false);
            Require(mover.Resizes==0 && !engine.Resizing,"drag changed into resize midway");
            engine.Process(new ContactFrame { Device=new IntPtr(1) },ConfirmMs + 30,false);
            int nextTouch = ConfirmMs + 40;
            engine.Process(Triangle(),nextTouch,false); engine.Process(Triangle(0.8),nextTouch + ConfirmMs,false);
            Require(engine.Resizing,"new gesture did not reclassify");
            int count=mover.Resizes; engine.Process(Triangle(0.8),nextTouch + ConfirmMs + 10,true);
            engine.Process(Triangle(1.1),nextTouch + ConfirmMs + 20,false);
            Require(!engine.Resizing && mover.Resizes==count,"Escape did not stop pinch");
        }
        internal static void GeometryAndBounds()
        {
            var original=new Rectangle(100,100,800,600); var cursor=new Point(300,250);
            Rectangle grown=ResizeGeometry.ScaleAroundAnchor(original,cursor,1.5,new Size(200,120),new Size(2560,1440));
            Require(grown == new Rectangle(0,25,1200,900),"anchor or aspect ratio drift");
            Rectangle small=ResizeGeometry.ScaleAroundAnchor(original,cursor,0.001,new Size(200,120),new Size(2560,1440));
            Require(small.Width>=200 && small.Height>=120,"minimum size lost");
            Rectangle large=ResizeGeometry.ScaleAroundAnchor(original,cursor,100,new Size(200,120),new Size(2560,1440));
            Require(large.Width<=2560 && large.Height<=1440,"maximum size lost");
            Rectangle[] areas={new Rectangle(0,0,2560,1440)};
            var desired=new Rectangle(200,-80,800,600);
            Require(WindowMover.KeepReachable(desired,areas,false).Y==0,"protected top did not stop");
            Require(WindowMover.KeepReachable(desired,areas,true).Y==-80,"free top still stopped at titlebar");
            Point extreme=WindowMover.KeepReachable(new Rectangle(200,-5000,800,600),areas,true);
            Require(extreme.Y+600>=64,"window fully lost above display");
            Require((WindowMover.MovementFlags(true,true,true)&4)==0,"front option did not permit Z order change");
            Require((WindowMover.MovementFlags(false,true,true)&4)!=0,"front option off changed Z order");
            Require((WindowMover.MovementFlags(true,false,true)&4)!=0,"every sample raised the window");
        }
        private static bool IsAbove(IntPtr upper, IntPtr lower)
        {
            for (int i=0; i<1000; i++) { lower=GetWindow(lower,3); if(lower==upper)return true; if(lower==IntPtr.Zero)return false; }
            return false;
        }
        internal static void NativeRaiseAndTopOverflow()
        {
            Require(Native.GetCursorPos(out Native.Point saved),"get cursor");
            try
            {
                using var back=new LatencyChecks.SlowWindow(true);
                using var front=new LatencyChecks.SlowWindow(true);
                Native.SetWindowPos(front.Handle,IntPtr.Zero,0,0,0,0,0x0013);
                // Previous maximize/restore tests can still have activation queued on another window.
                // Establish this fixture's foreground before testing the off/on transition.
                ForegroundActivation.Raise(front.Handle);
                Require(System.Threading.SpinWait.SpinUntil(() => Native.GetForegroundWindow() == front.Handle, 500), "fixture foreground did not settle");
                Require(IsAbove(front.Handle,back.Handle),"fixture Z order");
                using(var mover=new WindowMover { BringToFront=false,FollowCursor=false,MaximizeAtTop=false })
                {
                    Native.GetWindowRect(back.Handle,out Native.Rect rect);
                    Require(mover.PrepareWindow(back.Handle,new Native.Point(rect.Left+180,rect.Top+140)) && mover.Capture(),"capture background");
                    Require(mover.Move(10,10)&&mover.WaitForIdle(2000),"background move");
                    Require(IsAbove(front.Handle,back.Handle),"disabled foreground option raised window");
                }
                using(var mover=new WindowMover { BringToFront=true,FollowCursor=false,MaximizeAtTop=false })
                {
                    Native.GetWindowRect(back.Handle,out Native.Rect rect);
                    Require(mover.PrepareWindow(back.Handle,new Native.Point(rect.Left+180,rect.Top+140)) && mover.Capture(),"capture foreground option");
                    Require(mover.Move(10,10)&&mover.WaitForIdle(2000),"foreground move");
                    bool raised = System.Threading.SpinWait.SpinUntil(() => IsAbove(back.Handle,front.Handle), 500);
                    if (!raised)
                    {
                        Console.WriteLine("Foreground diagnostic: status="+mover.Status+"; foreground="+Native.GetForegroundWindow()+"; back="+back.Handle+"; front="+front.Handle);
                        foreach(int key in new[]{0x10,0x11,0x12,0x5B,0x5C}) Console.WriteLine("  Modifier "+key+" down="+((Native.GetAsyncKeyState(key)&0x8000)!=0));
                    }
                    Require(raised,"enabled foreground option did not raise window");
                }
                var area=MonitorLayout.ForWindow(back.Handle,true);
                using(var mover=new WindowMover { AllowTopOverflow=false,FollowCursor=false,MaximizeAtTop=false })
                {
                    Native.GetWindowRect(back.Handle,out Native.Rect rect);
                    Require(mover.PrepareWindow(back.Handle,new Native.Point(rect.Left+180,rect.Top+140))&&mover.Capture(),"capture protected top");
                    Require(mover.Move(0,area.Top-80-rect.Top)&&mover.WaitForIdle(2000),"protected top move");
                    Native.GetWindowRect(back.Handle,out rect); Require(rect.Top==area.Top,"protected top failed");
                }
                using(var mover=new WindowMover { AllowTopOverflow=true,FollowCursor=false,MaximizeAtTop=false })
                {
                    Native.GetWindowRect(back.Handle,out Native.Rect rect);
                    Require(mover.PrepareWindow(back.Handle,new Native.Point(rect.Left+180,rect.Top+140))&&mover.Capture(),"capture free top");
                    Require(mover.Move(0,-80)&&mover.WaitForIdle(2000),"free top move");
                    Native.GetWindowRect(back.Handle,out rect); Require(rect.Top==area.Top-80,"free top still stopped");
                }
            }
            finally { Native.SetCursorPos(saved.X,saved.Y); }
        }
        internal static void NativePinchResize()
        {
            Require(Native.GetCursorPos(out Native.Point saved),"get cursor");
            try
            {
                using var target=new LatencyChecks.SlowWindow(true);
                using var mover=new WindowMover { FollowCursor=true,MaximizeAtTop=true };
                Native.GetWindowRect(target.Handle,out Native.Rect before);
                var anchor=new Native.Point(before.Left+180,before.Top+140);
                Require(Native.SetCursorPos(anchor.X,anchor.Y),"set cursor");
                Require(mover.PrepareWindow(target.Handle,anchor)&&mover.CanResize&&mover.Capture(),"prepare resizable target");
                Require(mover.Resize(1.5)&&mover.WaitForIdle(2000),"native expansion");
                Native.GetWindowRect(target.Handle,out Native.Rect grown);
                Require(grown.Right-grown.Left==(int)Math.Round((before.Right-before.Left)*1.5),"expansion width");
                Require(grown.Bottom-grown.Top==(int)Math.Round((before.Bottom-before.Top)*1.5),"expansion height");
                Native.GetCursorPos(out Native.Point pointer);
                Require(Math.Abs(pointer.X-anchor.X)<=1&&Math.Abs(pointer.Y-anchor.Y)<=1,"resize cursor anchor drift");
                Require(mover.Resize(0.7)&&mover.WaitForIdle(2000),"native contraction");
                Native.GetWindowRect(target.Handle,out Native.Rect shrunk);
                Require(shrunk.Right-shrunk.Left==(int)Math.Round((before.Right-before.Left)*0.7),"contraction width accumulated");
                Require(!mover.TopEdgeArmed,"resize armed top maximum");
                mover.Finish(); Require(mover.WaitForIdle(2000)&&!Native.IsZoomed(target.Handle),"pinch release maximized window");
            }
            finally { Native.SetCursorPos(saved.X,saved.Y); }
        }
    }
}
