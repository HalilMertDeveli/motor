// Smartstore file system storage layout: Media/Storage/<id / 1000, 4 digits>/<id, 7 digits>.<ext>
import path from 'node:path';

const pad = (n, w) => String(n).padStart(w, '0');

export const storagePath = (mediaRoot, f) => path.join(mediaRoot, 'Storage', pad(Math.floor(f.Id / 1000), 4), `${pad(f.Id, 7)}.${f.Extension}`);
