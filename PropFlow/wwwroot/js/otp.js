// ===================================================
// OTP Page - Logic for timer, inputs, resend, submit
// ===================================================

// --- OTP COUNTDOWN TIMER (e.g. 2:45) ---
let otpTimerInterval = null;
let otpTimerSeconds = 165; // 2 min 45 sec

function startOtpTimer() {
    const timerEl = document.getElementById('otp-timer');
    if (!timerEl) return;

    clearInterval(otpTimerInterval);

    otpTimerInterval = setInterval(() => {
        if (otpTimerSeconds <= 0) {
            clearInterval(otpTimerInterval);
            timerEl.textContent = '00:00';
            timerEl.classList.remove('text-sky-600', 'dark:text-[#00f2ff]');
            timerEl.classList.add('text-red-500');

            const submitBtn = document.getElementById('otp-submit-btn');
            if (submitBtn) {
                submitBtn.disabled = true;
                submitBtn.classList.add('opacity-50', 'cursor-not-allowed');
            }
            return;
        }
        otpTimerSeconds--;
        const m = String(Math.floor(otpTimerSeconds / 60)).padStart(2, '0');
        const s = String(otpTimerSeconds % 60).padStart(2, '0');
        timerEl.textContent = `${m}:${s}`;
    }, 1000);
}

// --- RESEND COUNTDOWN (59s cooldown) ---
let resendInterval = null;
let resendSeconds = 59;

function startResendCountdown() {
    const resendCountdownEl = document.getElementById('resend-countdown');
    const resendBtn = document.getElementById('resend-btn');
    if (!resendCountdownEl || !resendBtn) return;

    resendBtn.disabled = true;
    resendBtn.classList.add('opacity-50', 'cursor-not-allowed');
    resendSeconds = 59;

    clearInterval(resendInterval);
    resendInterval = setInterval(() => {
        resendSeconds--;
        if (resendCountdownEl) resendCountdownEl.textContent = `${resendSeconds}s`;

        if (resendSeconds <= 0) {
            clearInterval(resendInterval);
            if (resendCountdownEl) resendCountdownEl.textContent = 'Gửi lại';
            if (resendBtn) {
                resendBtn.disabled = false;
                resendBtn.classList.remove('opacity-50', 'cursor-not-allowed');
            }
        }
    }, 1000);
}

// --- OTP INPUT AUTO-FOCUS NAVIGATION ---
window.onOtpInput = function(el, index) {
    // Only allow digits
    el.value = el.value.replace(/[^0-9]/g, '').slice(-1);

    const inputs = document.querySelectorAll('#otp-inputs-wrapper input');

    if (el.value && index < inputs.length - 1) {
        inputs[index + 1].focus();
    }

    // Auto-submit if all filled
    const allFilled = Array.from(inputs).every(inp => inp.value !== '');
    if (allFilled) {
        setTimeout(() => handleOtpSubmit(), 200);
    }
};

// Handle backspace key to go to previous input
window.onOtpKeydown = function(el, index, event) {
    if (event.key === 'Backspace' && !el.value && index > 0) {
        const inputs = document.querySelectorAll('#otp-inputs-wrapper input');
        inputs[index - 1].focus();
    }
};

// --- OTP SUBMIT ---
window.handleOtpSubmit = function() {
    const inputs = document.querySelectorAll('#otp-inputs-wrapper input');
    const otp = Array.from(inputs).map(i => i.value).join('');

    if (otp.length < 6) {
        // Shake animation on incomplete
        const wrapper = document.getElementById('otp-inputs-wrapper');
        if (wrapper) {
            wrapper.classList.add('animate-bounce');
            setTimeout(() => wrapper.classList.remove('animate-bounce'), 600);
        }
        return;
    }

    const btn = document.getElementById('otp-submit-btn');
    if (!btn) return;

    const origHTML = btn.innerHTML;
    btn.innerHTML = '<span class="material-symbols-outlined animate-spin text-[18px]">sync</span><span>ĐANG XÁC THỰC...</span>';
    btn.disabled = true;

    // Simulate async verification
    setTimeout(() => {
        // Success state
        btn.innerHTML = '<span class="material-symbols-outlined text-[18px]">check_circle</span><span>XÁC THỰC THÀNH CÔNG!</span>';
        btn.classList.remove('bg-sky-600', 'dark:bg-[#00f2ff]');
        btn.classList.add('bg-emerald-600', 'dark:bg-emerald-500');

        setTimeout(() => {
            // Navigate to reset password page
            window.location.href = '/reset-password';
        }, 900);
    }, 1000);
};

// --- RESEND OTP ---
window.handleResendOtp = function() {
    const resendBtn = document.getElementById('resend-btn');
    if (resendBtn && resendBtn.disabled) return;

    // Reset timer
    otpTimerSeconds = 165;
    const timerEl = document.getElementById('otp-timer');
    if (timerEl) {
        timerEl.textContent = '02:45';
        timerEl.classList.add('text-sky-600');
        timerEl.classList.remove('text-red-500');
    }

    // Reset submit btn
    const submitBtn = document.getElementById('otp-submit-btn');
    if (submitBtn) {
        submitBtn.disabled = false;
        submitBtn.classList.remove('opacity-50', 'cursor-not-allowed');
    }

    // Clear inputs
    const inputs = document.querySelectorAll('#otp-inputs-wrapper input');
    inputs.forEach(inp => inp.value = '');
    if (inputs.length > 0) inputs[0].focus();

    startOtpTimer();
    startResendCountdown();
};

// --- INIT ---
window.initOtpPage = function() {
    startOtpTimer();
    startResendCountdown();

    // Paste support: paste full OTP code into first input
    const wrapper = document.getElementById('otp-inputs-wrapper');
    if (wrapper) {
        wrapper.addEventListener('paste', (e) => {
            e.preventDefault();
            const pasted = (e.clipboardData || window.clipboardData).getData('text').replace(/\D/g, '').slice(0, 6);
            const inputs = wrapper.querySelectorAll('input');
            pasted.split('').forEach((char, i) => {
                if (inputs[i]) inputs[i].value = char;
            });
            const nextEmpty = Array.from(inputs).find(inp => !inp.value);
            if (nextEmpty) nextEmpty.focus();
            else if (inputs.length > 0) inputs[inputs.length - 1].focus();

            const allFilled = Array.from(inputs).every(inp => inp.value !== '');
            if (allFilled) setTimeout(() => handleOtpSubmit(), 200);
        });
    }
};
