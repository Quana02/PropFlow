export function initialize(icon, temperature, description, date, clock) {
    const zone = "Asia/Ho_Chi_Minh";
    const dateFormatter = new Intl.DateTimeFormat("vi-VN", {
        timeZone: zone, weekday: "long", day: "2-digit", month: "2-digit", year: "numeric"
    });
    const timeFormatter = new Intl.DateTimeFormat("vi-VN", {
        timeZone: zone, hour: "2-digit", minute: "2-digit", hourCycle: "h23"
    });
    let disposed = false;
    let weatherRequest;

    function updateClock() {
        const now = new Date();
        date.textContent = dateFormatter.format(now);
        clock.textContent = timeFormatter.format(now);
    }

    function weatherSymbol(code, isDay) {
        if (code === 0) return isDay ? "☀" : "☾";
        if (code <= 3) return "☁";
        if (code <= 48) return "≋";
        if (code <= 67 || code >= 80 && code <= 82) return "☂";
        if (code >= 95) return "⚡";
        return "☁";
    }

    function weatherText(code) {
        if (code === 0) return "Trời quang";
        if (code <= 3) return "Có mây";
        if (code <= 48) return "Sương mù";
        if (code <= 67 || code >= 80 && code <= 82) return "Có mưa";
        if (code >= 95) return "Dông";
        return "Nhiều mây";
    }

    async function updateWeather() {
        weatherRequest?.abort();
        weatherRequest = new AbortController();
        try {
            const url = "https://api.open-meteo.com/v1/forecast?latitude=10.8231&longitude=106.6297&current=temperature_2m,weather_code,is_day&timezone=Asia%2FHo_Chi_Minh";
            const response = await fetch(url, { signal: weatherRequest.signal });
            if (!response.ok) throw new Error(`Weather HTTP ${response.status}`);
            const data = await response.json();
            if (disposed || !Number.isFinite(data.current?.temperature_2m)) return;
            const code = data.current.weather_code;
            temperature.textContent = `${Math.round(data.current.temperature_2m)}°C · ${weatherText(code)}`;
            description.textContent = "TP. Hồ Chí Minh";
            icon.textContent = weatherSymbol(code, data.current.is_day === 1);
        } catch (error) {
            if (disposed || error.name === "AbortError") return;
            temperature.textContent = "Thời tiết chưa có";
            description.textContent = "TP. Hồ Chí Minh";
            icon.textContent = "☁";
        }
    }

    updateClock();
    updateWeather();
    const clockTimer = window.setInterval(updateClock, 15_000);
    const weatherTimer = window.setInterval(updateWeather, 20 * 60_000);

    return {
        dispose() {
            disposed = true;
            weatherRequest?.abort();
            window.clearInterval(clockTimer);
            window.clearInterval(weatherTimer);
        }
    };
}
