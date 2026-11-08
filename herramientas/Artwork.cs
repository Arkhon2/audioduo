using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
public static class AudioDuoArtwork { public static void Main(string[] args) { Directory.CreateDirectory(args[0]); Save(args[0]); }
 static GraphicsPath Round(float x,float y,float w,float h,float r) {
  var p=new GraphicsPath(); float d=r*2;
  p.AddArc(x,y,d,d,180,90); p.AddArc(x+w-d,y,d,d,270,90);
  p.AddArc(x+w-d,y+h-d,d,d,0,90); p.AddArc(x,y+h-d,d,d,90,90); p.CloseFigure(); return p;
 }
 public static void Save(string folder) {
  using(var image=new Bitmap(256,256)) {
   using(var g=Graphics.FromImage(image)) {
    g.SmoothingMode=SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
    using(var path=Round(4,4,248,248,54))
    using(var fill=new LinearGradientBrush(new Point(20,5),new Point(240,250),Color.FromArgb(31,54,81),Color.FromArgb(12,22,37)))
    using(var border=new Pen(Color.FromArgb(61,86,114),2)) { g.FillPath(fill,path); g.DrawPath(border,path); }
    for(int i=0;i<2;i++) {
     int x=i==0?44:164; Color c=i==0?Color.FromArgb(94,170,255):Color.FromArgb(58,221,184);
     using(var p=Round(x,65,48,126,13)) using(var fill=new SolidBrush(c)) g.FillPath(fill,p);
     using(var b=new SolidBrush(Color.FromArgb(19,36,54))) {g.FillEllipse(b,x+14,83,20,20);g.FillEllipse(b,x+8,127,32,32);}
    }
    var points=new PointF[71];
    for(int i=0;i<points.Length;i++) points[i]=new PointF(101+i*54f/70,128-(float)Math.Sin(i*Math.PI*4/70)*17);
    using(var line=new Pen(Color.FromArgb(224,241,255),6)) {line.StartCap=line.EndCap=LineCap.Round;g.DrawLines(line,points);}
   }
   image.Save(Path.Combine(folder,"AudioDuo.png"),ImageFormat.Png);
   int[] sizes={16,24,32,48,64,128,256}; byte[][] data=new byte[sizes.Length][];
   for(int i=0;i<sizes.Length;i++) using(var small=new Bitmap(sizes[i],sizes[i])) {
    using(var g=Graphics.FromImage(small)) {g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(image,0,0,sizes[i],sizes[i]);}
    using(var stream=new MemoryStream()) {small.Save(stream,ImageFormat.Png);data[i]=stream.ToArray();}
   }
   using(var w=new BinaryWriter(File.Create(Path.Combine(folder,"AudioDuo.ico")))) {
    w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length); int offset=6+16*sizes.Length;
    for(int i=0;i<sizes.Length;i++) {w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)0);w.Write((byte)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(data[i].Length);w.Write(offset);offset+=data[i].Length;}
    foreach(var bytes in data) w.Write(bytes);
   }
  }
 }
}
