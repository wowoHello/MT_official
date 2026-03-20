/**
 * Login Module
 * Handles UI interactions, canvas captcha, font-size adjustment, and fake login flow.
 * Version: 1.0 (DEMO)
 */

// --- Constants & Global State ---
let generatedCaptcha = '';

// DOM Elements
const form = document.getElementById('loginForm');
const userInput = document.getElementById('username');
const pwdInput = document.getElementById('password');
const captchaInput = document.getElementById('captcha');
const rememberMeCheckbox = document.getElementById('rememberMe');
const togglePwdBtn = document.getElementById('togglePasswordBtn');
const eyeIcon = document.getElementById('eyeIcon');
const submitBtn = document.getElementById('submitBtn');
const btnText = document.getElementById('btnText');
const btnIcon = document.getElementById('btnIcon');
const btnSpinner = document.getElementById('btnSpinner');
const loginAlert = document.getElementById('loginAlert');
const loginAlertMsg = document.getElementById('loginAlertMsg');

// --- Initialization ---
document.addEventListener('DOMContentLoaded', () => {
    // Set current year dynamically
    document.getElementById('currentYear').textContent = new Date().getFullYear();

    // Initialize Captcha
    refreshCaptcha();

    // Check remembered account
    checkRememberedAccount();
    
    // Bind Events
    document.getElementById('captchaCanvas').addEventListener('click', refreshCaptcha);
    document.getElementById('refreshCaptchaBtn').addEventListener('click', refreshCaptcha);
    togglePwdBtn.addEventListener('click', togglePasswordVisibility);
    
    // Form submission
    form.addEventListener('submit', handleLogin);
    
    // Forgot Password Modal
    initForgotPwdModal();
});

// --- Captcha Engine ---
/**
 * 生成大小寫字母與數字混和的隨機字串（排除容易混淆的字元）
 * @param {number} length 字數長度
 * @returns {string}
 */
function generateRandomString(length) {
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789'; // Excluded I, l, 1, O, 0
    let result = '';
    for (let i = 0; i < length; i++) {
        result += chars.charAt(Math.floor(Math.random() * chars.length));
    }
    return result;
}

/**
 * 重新繪製帶有干擾線與噪點的驗證碼 Canvas
 */
function refreshCaptcha() {
    const canvas = document.getElementById('captchaCanvas');
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    const width = canvas.width;
    const height = canvas.height;
    
    // Clear canvas
    ctx.clearRect(0, 0, width, height);
    
    // Generate text
    generatedCaptcha = generateRandomString(6);
    
    // Background color
    ctx.fillStyle = '#f8fafc';
    ctx.fillRect(0, 0, width, height);
    
    // Draw interference lines
    for (let i = 0; i < 6; i++) {
        ctx.strokeStyle = `rgba(${Math.random()*255},${Math.random()*255},${Math.random()*255}, 0.5)`;
        ctx.beginPath();
        ctx.moveTo(Math.random() * width, Math.random() * height);
        ctx.lineTo(Math.random() * width, Math.random() * height);
        ctx.stroke();
    }
    
    // Draw noise dots
    for (let i = 0; i < 40; i++) {
        ctx.fillStyle = `rgba(${Math.random()*255},${Math.random()*255},${Math.random()*255}, 0.7)`;
        ctx.beginPath();
        ctx.arc(Math.random() * width, Math.random() * height, 1, 0, 2 * Math.PI);
        ctx.fill();
    }
    
    // Draw text with variations
    ctx.font = 'bold 24px "Courier New", monospace';
    ctx.textBaseline = 'middle';
    
    for (let i = 0; i < generatedCaptcha.length; ++i) {
        const char = generatedCaptcha[i];
        
        ctx.fillStyle = '#374151'; // Slate main color
        // Random scale and rotate for better security simulation
        ctx.save();
        ctx.translate(20 + (i * 20), height / 2 + (Math.random() * 6 - 3));
        ctx.rotate((Math.random() * 0.4 - 0.2));
        ctx.fillText(char, 0, 0);
        ctx.restore();
    }
    
    // [Blazor Migration Note] Canvas 繪圖邏輯在 Blazor 伺服器端渲染時較難以原生 C# 執行。
    // 遷移時，建議保留 JavaScript Interop 方式呼叫此 js 函式來進行驗證碼重繪，
    // 或改由後端直接產出 Base64 圖片並回傳給 <img> 標籤。
}

// --- Password Visibility Toggle ---
function togglePasswordVisibility() {
    const isPassword = pwdInput.getAttribute('type') === 'password';
    pwdInput.setAttribute('type', isPassword ? 'text' : 'password');
    
    if (isPassword) {
        eyeIcon.classList.remove('fa-eye-slash');
        eyeIcon.classList.add('fa-eye');
    } else {
        eyeIcon.classList.remove('fa-eye');
        eyeIcon.classList.add('fa-eye-slash');
    }
}

// --- Remember Me Logic ---
function checkRememberedAccount() {
    const savedAccount = localStorage.getItem('cwt_remembered_account');
    const savedTime = localStorage.getItem('cwt_remembered_time');
    
    if (savedAccount && savedTime) {
        const timeDiff = new Date().getTime() - parseInt(savedTime, 10);
        const daysDiff = timeDiff / (1000 * 3600 * 24);
        
        if (daysDiff <= 90) {
            userInput.value = savedAccount;
            rememberMeCheckbox.checked = true;
        } else {
            // Expired after 90 days
            localStorage.removeItem('cwt_remembered_account');
            localStorage.removeItem('cwt_remembered_time');
        }
    }
}

// --- Login Handler ---
async function handleLogin(e) {
    e.preventDefault();
    
    const user = userInput.value.trim();
    const pwd = pwdInput.value.trim();
    const captcha = captchaInput.value.trim();
    
    // Clear alerts
    loginAlert.classList.add('hidden');
    
    // Basic Validations
    if (!user || !pwd) {
        showError('請輸入完整的帳號密碼。');
        return;
    }
    
    if (captcha.toLowerCase() !== generatedCaptcha.toLowerCase()) {
        showError('驗證碼錯誤，請重新輸入。');
        captchaInput.value = '';
        refreshCaptcha();
        captchaInput.focus();
        return;
    }
    
    // Simulated Login Process execution
    setLoadingState(true);
    
    try {
        // Fake API Call Delay
        await new Promise(resolve => setTimeout(resolve, 1200));
        
        // Setup mock user info in localStorage for other pages demo use
        const userInfo = {
            id: 'T1001',
            name: user === 'admin' ? '系統管理員' : '劉老師',
            role: user === 'admin' ? 'ADMIN' : 'TEACHER',
            loginTime: new Date().toISOString()
        };
        localStorage.setItem('cwt_user', JSON.stringify(userInfo));
        
        // Handle Remember Me
        if (rememberMeCheckbox.checked) {
            localStorage.setItem('cwt_remembered_account', user);
            localStorage.setItem('cwt_remembered_time', new Date().getTime().toString());
        } else {
            localStorage.removeItem('cwt_remembered_account');
            localStorage.removeItem('cwt_remembered_time');
        }

        // Success Toast Notification via SweetAlert2
        await Swal.fire({
            icon: 'success',
            title: '登入成功',
            text: '即將進入命題工作平臺...',
            timer: 1000,
            showConfirmButton: false,
            backdrop: `rgba(0,0,0,0.4)`
        });
        
        // Redirect to first page (dashboard hub)
        window.location.href = 'firstpage.html';
        
    } catch (err) {
        showError('系統登入發生例外錯誤，請稍後再試。');
        setLoadingState(false);
    }
    
    // [Blazor Migration Note] 未來轉移至 Blazor 時，請將此處的 setTimeout 假延遲與 LocalStorage 操作，
    // 替換為實際的 C# HttpClient 登入要求呼叫，並透過 AuthenticationStateProvider 處理與維護 JWT / Session 使用者證書狀態，
    // 前端防呆可維持現有邏輯，但核心登入驗證必須交由後端處理。
}

function showError(msg) {
    loginAlertMsg.textContent = msg;
    loginAlert.classList.remove('hidden');
    // Simple shake animation using tailwind + js to grab attention
    form.classList.add('animate-pulse');
    setTimeout(() => form.classList.remove('animate-pulse'), 400);
}

function setLoadingState(isLoading) {
    submitBtn.disabled = isLoading;
    if (isLoading) {
        btnText.textContent = '登入中...';
        btnIcon.classList.add('hidden');
        btnSpinner.classList.remove('hidden');
        userInput.disabled = true;
        pwdInput.disabled = true;
        captchaInput.disabled = true;
    } else {
        btnText.textContent = '登入系統';
        btnIcon.classList.remove('hidden');
        btnSpinner.classList.add('hidden');
        userInput.disabled = false;
        pwdInput.disabled = false;
        captchaInput.disabled = false;
    }
}



// --- Forgot Password Modal ---
function initForgotPwdModal() {
    const modal = document.getElementById('forgotPwdModal');
    const panel = document.getElementById('forgotPwdPanel');
    const openBtn = document.getElementById('forgotPwdBtn');
    const closeBtns = document.querySelectorAll('.close-modal-btn');
    const backdrops = document.querySelectorAll('.modal-backdrop');
    
    const step1 = document.getElementById('step1Container');
    const step2 = document.getElementById('step2Container');
    const sendBtn = document.getElementById('sendResetLinkBtn');
    const confirmBtn = document.getElementById('confirmResetBtn');
    const resetEmailInput = document.getElementById('resetEmail');
    
    const newPwd = document.getElementById('newPassword');
    const confirmPwd = document.getElementById('confirmNewPassword');
    const errorHint = document.getElementById('resetPwdError');
    const oldPwdDemoVal = document.getElementById('demoOldPwd').value;

    const openModal = () => {
        // Start fresh every time modal opens
        step1.classList.remove('hidden');
        step2.classList.add('hidden');
        resetEmailInput.value = '';
        newPwd.value = '';
        confirmPwd.value = '';
        errorHint.classList.add('hidden');
        
        modal.classList.remove('hidden');
        
        // Trigger reflow to ensure CSS transitions play correctly
        void modal.offsetWidth;
        panel.classList.remove('scale-95', 'opacity-0');
        panel.classList.add('scale-100', 'opacity-100');
    };

    const closeModal = () => {
        panel.classList.remove('scale-100', 'opacity-100');
        panel.classList.add('scale-95', 'opacity-0');
        setTimeout(() => {
            modal.classList.add('hidden');
        }, 300); // 配合 CSS transition durations
    };

    openBtn.addEventListener('click', openModal);
    closeBtns.forEach(btn => btn.addEventListener('click', closeModal));
    backdrops.forEach(bg => bg.addEventListener('click', closeModal));
    
    // Step 1: Send reset link simulation (假裝寄信)
    sendBtn.addEventListener('click', async () => {
        const email = resetEmailInput.value.trim();
        if (!email) {
            Swal.fire({ icon: 'error', title: '錯誤', text: '請輸入電子信箱', confirmButtonColor: '#6B8EAD' });
            return;
        }
        
        sendBtn.disabled = true;
        const originalText = sendBtn.textContent;
        sendBtn.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> 發送中...';
        
        await new Promise(r => setTimeout(r, 800)); // fake network delay
        
        Swal.fire({
            icon: 'success', 
            title: '信件已發送', 
            text: '重設連結已發送至您的信箱！(此為 Demo，將直接導向重設畫面)',
            timer: 2500,
            showConfirmButton: false
        }).then(() => {
            // 切換至 Demo Step 2 (重設密碼)
            step1.classList.add('hidden');
            step2.classList.remove('hidden');
            sendBtn.disabled = false;
            sendBtn.textContent = originalText;
        });
    });
    
    // Step 2: Confirm new password simulation
    confirmBtn.addEventListener('click', async () => {
        errorHint.classList.add('hidden');
        
        const np = newPwd.value.trim();
        const cp = confirmPwd.value.trim();
        
        if (!np || !cp) {
            Swal.fire({ icon: 'warning', title: '提示', text: '請完整填寫新密碼與確認密碼。', confirmButtonColor: '#6B8EAD' });
            return;
        }
        
        if (np !== cp) {
            Swal.fire({ icon: 'warning', title: '輸入不符', text: '兩次輸入的新密碼不同！', confirmButtonColor: '#6B8EAD' });
            return;
        }
        
        // 防呆規範要求：新密碼不可與舊密碼相同
        if (np === oldPwdDemoVal) {
            errorHint.classList.remove('hidden');
            return; // 立即阻止送出
        }
        
        confirmBtn.disabled = true;
        confirmBtn.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> 儲存中...';
        
        await new Promise(r => setTimeout(r, 1000));
        
        await Swal.fire({
            icon: 'success',
            title: '密碼重設成功',
            text: '請使用您的新密碼重新登入。',
            timer: 2000,
            showConfirmButton: false
        });
        
        confirmBtn.disabled = false;
        confirmBtn.textContent = '儲存變更';
        closeModal();
    });
}
