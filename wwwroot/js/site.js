// =============================================================================
// site.js — Core JavaScript (Phase 2 & beyond)
// =============================================================================
// Contains logic for:
// - Cyber Carousel (Terminal loading bar & slide controls)
// =============================================================================

document.addEventListener('DOMContentLoaded', () => {
    initCyberCarousel();
});

function initCyberCarousel() {
    const track = document.getElementById('hero-carousel-track');
    const prevBtn = document.getElementById('carousel-prev');
    const nextBtn = document.getElementById('carousel-next');
    const statusEl = document.getElementById('carousel-status');
    
    if (!track || !prevBtn || !nextBtn || !statusEl) return;
    
    const slides = track.querySelectorAll('.cyber-carousel-slide');
    if (slides.length <= 1) {
        statusEl.innerHTML = `Status: <span style="color: var(--text-muted);">[██████████]</span> 100%`;
        prevBtn.style.opacity = '0.5';
        nextBtn.style.opacity = '0.5';
        return; // No need to slide if 1 or 0 items
    }
    
    let currentIndex = 0;
    const totalSlides = slides.length;
    const slideDuration = 5000; // 5 seconds per slide
    const tickInterval = 50; // Update progress every 50ms
    let progress = 0;
    let timer = null;
    
    const updateSlide = () => {
        track.style.transform = `translateX(-${currentIndex * 100}%)`;
    };
    
    const updateProgressUI = () => {
        const percent = Math.min(100, Math.floor((progress / slideDuration) * 100));
        const blocks = 10;
        const filled = Math.round((percent / 100) * blocks);
        const empty = blocks - filled;
        
        const bar = '█'.repeat(filled) + '░'.repeat(empty);
        statusEl.innerHTML = `Status: <span style="color: var(--accent);">[${bar}]</span> ${percent}%`;
    };
    
    const tick = () => {
        progress += tickInterval;
        
        if (progress >= slideDuration) {
            progress = 0;
            currentIndex = (currentIndex + 1) % totalSlides;
            updateSlide();
        }
        
        updateProgressUI();
    };
    
    const resetTimer = () => {
        progress = 0;
        if (timer) clearInterval(timer);
        timer = setInterval(tick, tickInterval);
        updateProgressUI();
    };
    
    prevBtn.addEventListener('click', () => {
        currentIndex = (currentIndex - 1 + totalSlides) % totalSlides;
        updateSlide();
        resetTimer();
    });
    
    nextBtn.addEventListener('click', () => {
        currentIndex = (currentIndex + 1) % totalSlides;
        updateSlide();
        resetTimer();
    });
    
    // Pause on hover
    const container = document.querySelector('.cyber-carousel-container');
    if (container) {
        container.addEventListener('mouseenter', () => {
            if (timer) clearInterval(timer);
            // Optionally blink the cursor to show it's paused
            statusEl.innerHTML = statusEl.innerHTML.replace('Status:', 'PAUSED:');
        });
        container.addEventListener('mouseleave', () => {
            statusEl.innerHTML = statusEl.innerHTML.replace('PAUSED:', 'Status:');
            timer = setInterval(tick, tickInterval);
        });
    }
    
    // Start the loop
    resetTimer();
}
