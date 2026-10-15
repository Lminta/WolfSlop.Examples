(() => {
    const maps = new WeakMap();
    const size = 256;
    const padding = 10;

    window.wolfMinimap = {
        setMap(canvas, worldMap) {
            const width = worldMap.length;
            const height = worldMap[0].length;
            const scale = (size - padding * 2) / Math.max(width, height);
            canvas.width = Math.ceil(width * scale + padding * 2);
            canvas.height = Math.ceil(height * scale + padding * 2);

            // Cache the static walls; only markers are redrawn each frame.
            const background = document.createElement("canvas");
            background.width = canvas.width;
            background.height = canvas.height;
            const context = background.getContext("2d");
            context.fillStyle = "#111923";
            context.fillRect(0, 0, background.width, background.height);

            for (let x = 0; x < width; x++) {
                for (let y = 0; y < height; y++) {
                    context.fillStyle = worldMap[x][y] === 0 ? "#23313d" : "#82919e";
                    context.fillRect(padding + x * scale, padding + y * scale, scale, scale);
                    context.strokeStyle = "#111923";
                    context.lineWidth = 0.5;
                    context.strokeRect(padding + x * scale, padding + y * scale, scale, scale);
                }
            }

            maps.set(canvas, { context: canvas.getContext("2d"), background, scale });
        },

        draw(canvas, x, y, dirX, dirY, planeX, planeY, sprites) {
            const map = maps.get(canvas);
            if (!map) return;
            const { context, background, scale } = map;
            context.clearRect(0, 0, canvas.width, canvas.height);
            context.drawImage(background, 0, 0);
            context.save();
            // Keep world x horizontal and world y vertical, matching WorldMap[x][y].
            context.translate(padding, padding);
            context.scale(scale, scale);

            context.fillStyle = "rgba(69, 191, 255, 0.18)";
            context.beginPath();
            context.moveTo(x, y);
            context.lineTo(x + (dirX - planeX) * 3, y + (dirY - planeY) * 3);
            context.lineTo(x + (dirX + planeX) * 3, y + (dirY + planeY) * 3);
            context.closePath();
            context.fill();

            for (let i = 0; i < sprites.length; i += 3) {
                context.fillStyle = sprites[i + 2] === 10 ? "#7ce66d" :
                    sprites[i + 2] === 9 ? "#dae1e7" : "#e4ad61";
                context.beginPath();
                context.arc(sprites[i], sprites[i + 1], 0.18, 0, Math.PI * 2);
                context.fill();
            }

            // An arrow makes both player position and heading easy to see.
            context.translate(x, y);
            context.rotate(Math.atan2(dirY, dirX));
            context.fillStyle = "#45bfff";
            context.strokeStyle = "#09131c";
            context.lineWidth = 0.12;
            context.beginPath();
            context.moveTo(0.55, 0);
            context.lineTo(-0.3, -0.32);
            context.lineTo(-0.18, 0);
            context.lineTo(-0.3, 0.32);
            context.closePath();
            context.fill();
            context.stroke();
            context.restore();
        },

        remove(canvas) {
            maps.delete(canvas);
        }
    };
})();
