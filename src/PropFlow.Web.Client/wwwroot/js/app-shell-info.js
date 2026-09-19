export function initialize(icon, temperature, description, date, clock, period) {
  const zone = "Asia/Ho_Chi_Minh";
  const weekdayFormatter = new Intl.DateTimeFormat("vi-VN", { timeZone: zone, weekday: "long" });
  const dayFormatter = new Intl.DateTimeFormat("vi-VN", { timeZone: zone, day: "numeric" });
  const monthFormatter = new Intl.DateTimeFormat("vi-VN", { timeZone: zone, month: "numeric" });
  const yearFormatter = new Intl.DateTimeFormat("vi-VN", { timeZone: zone, year: "numeric" });
  const hourFormatter = new Intl.DateTimeFormat("vi-VN", {
    timeZone: zone, hour: "2-digit", minute: "2-digit", hourCycle: "h12"
  });
  let disposed = false;
  let weatherRequest;

  function capitalize(text) {
    return text.charAt(0).toUpperCase() + text.slice(1);
  }

  function formatDate(now) {
    const weekday = capitalize(weekdayFormatter.format(now));
    const day = dayFormatter.format(now);
    const month = monthFormatter.format(now);
    const year = yearFormatter.format(now);
    return `${weekday}, ${day} Tháng ${month}, ${year}`;
  }

  function formatTimeParts(now) {
    // hourFormatter với hourCycle "h12" trả về dạng "9:12 CH" hoặc "9:12 SA"
    // (vi-VN dùng SA/CH thay vì AM/PM). Tách phần giờ:phút và phần buổi ra riêng.
    const parts = hourFormatter.formatToParts(now);
    const hour = parts.find(p => p.type === "hour")?.value ?? "--";
    const minute = parts.find(p => p.type === "minute")?.value ?? "--";
    const dayPeriod = parts.find(p => p.type === "dayPeriod")?.value ?? "";
    const normalizedPeriod = dayPeriod.toUpperCase().includes("CH") ? "PM"
      : dayPeriod.toUpperCase().includes("SA") ? "AM"
        : dayPeriod.toUpperCase();
    return { time: `${hour.padStart(2, "0")}:${minute}`, period: normalizedPeriod };
  }

  function updateClock() {
    const now = new Date();
    date.textContent = formatDate(now);
    const { time, period: dayPeriod } = formatTimeParts(now);
    clock.textContent = time;
    if (period) period.textContent = dayPeriod;
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
