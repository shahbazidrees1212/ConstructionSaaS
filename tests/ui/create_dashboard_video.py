"""Render the existing dashboard illustration as a silent, seamless camera-motion loop."""
import sys,subprocess
from pathlib import Path
sys.path.insert(0,str(Path('.tools/video').resolve()))
import imageio_ffmpeg
out=Path('ConstructionSaaS.Web/wwwroot/videos/construction-overview.mp4');out.parent.mkdir(exist_ok=True)
subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(),'-y','-i','ConstructionSaaS.Web/wwwroot/images/construction-login.png','-vf',"scale=1920:-2,zoompan=z='1.02+0.04*(1-cos(on*2*PI/240))/2':x='iw/2-iw/zoom/2':y='ih/2-ih/zoom/2':d=240:s=1280x720:fps=30",'-frames:v','240','-an','-c:v','libx264','-preset','fast','-crf','25','-pix_fmt','yuv420p','-movflags','+faststart','-threads','2',str(out)],check=True)
print(f'Created {out}: {out.stat().st_size} bytes')
